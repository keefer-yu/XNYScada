using System.Collections.Concurrent;
using SqlSugar;
using XNY.Scada.Application.Flows;
using XNY.Scada.Application.Settings;
using XNY.Scada.Application.Templates;
using XNY.Scada.Device.Runtime;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Domain.Services;
using XNY.Scada.Flow.Runtime;
using Xunit;

namespace XNY.Scada.Tests;

/// <summary>四类机型模板：导入后用模拟 PLC 驱动握手，验证完整业务链路</summary>
public class MachineTemplateTests
{
    private sealed class Machine : IAsyncDisposable
    {
        public required TestHost Host { get; init; }
        public required string Sim { get; init; }
        public ConcurrentQueue<FlowInstance> Completed { get; } = new();
        public ConcurrentQueue<CellRecordEntity> Records { get; } = new();

        public async Task<int> CycleAsync(string req, string done, string result, params (string Address, object Value)[] data)
        {
            var before = Completed.Count;
            foreach (var (a, v) in data) SimulatorMemory.Set(Sim, a, v);
            SimulatorMemory.Set(Sim, req, true);
            Assert.True(await Host.WaitUntilAsync(() => Equals(SimulatorMemory.Read(Sim, done), true), 8000), $"{done} 未置位。{Failures()}");
            var value = Convert.ToInt32(SimulatorMemory.Read(Sim, result));
            SimulatorMemory.Set(Sim, req, false);
            Assert.True(await Host.WaitUntilAsync(() => !Equals(SimulatorMemory.Read(Sim, done), true), 8000), $"{done} 未复位");
            Assert.True(await Host.WaitUntilAsync(() => Completed.Count > before, 8000), "流程实例未结束");
            Assert.True(Completed.All(i => i.Status == FlowInstanceStatus.Completed), Failures());
            return value;
        }

        public string Failures() => string.Join("; ", Completed.Where(i => i.Status != FlowInstanceStatus.Completed)
            .Select(i => $"{i.Document.Code}@{i.CurrentNodeId}: {i.Error}"));

        public Task<List<NgRecordEntity>> NgRecordsAsync() =>
            Host.Get<ISqlSugarClient>().Queryable<NgRecordEntity>().SplitTable(t => t).OrderBy(r => r.Id).ToListAsync();

        public ValueTask DisposeAsync() => Host.DisposeAsync();
    }

    private static async Task<Machine> StartAsync(string machineType, Action<MachineTemplate>? customize = null)
    {
        var h = await TestHost.StartAsync();
        var sim = Guid.NewGuid().ToString("N");
        var template = MachineTemplate.Load(Path.Combine(MachineTemplateService.TemplateDirectory, $"{machineType}.json"));
        foreach (var d in template.Devices) d.Options["Instance"] = sim;
        customize?.Invoke(template);

        var m = new Machine { Host = h, Sim = sim };
        h.Get<IFlowEngine>().InstanceCompleted += (_, i) => m.Completed.Enqueue(i);
        h.Get<IProductionService>().RecordSaved += (_, r) => m.Records.Enqueue(r);

        var result = await h.Get<MachineTemplateService>().ImportAsync(template, replaceDevices: true, checkPermission: false);
        Assert.Empty(result.Warnings);
        Assert.Equal(machineType, h.Get<SystemParamService>().MachineType);
        Assert.True(await h.WaitUntilAsync(() => h.Get<Device.ITagService>().GetValue("PLC1.Auto")?.Quality == Device.TagQuality.Good), "模拟 PLC 未连接");
        return m;
    }

    [Fact]
    public void AllTemplates_Load_AndCoverEveryMachineType()
    {
        var files = Directory.GetFiles(MachineTemplateService.TemplateDirectory, "*.json");
        var templates = files.Select(MachineTemplate.Load).ToList();
        Assert.Equal(
            new[] { MachineTypes.Grading, MachineTypes.NgSorting, MachineTypes.TrayAssembly, MachineTypes.TrayDisassembly }.Order(),
            templates.Select(t => t.MachineType).Order());
        Assert.All(templates, t =>
        {
            Assert.NotEmpty(t.Devices);
            Assert.NotEmpty(t.Flows);
            Assert.Contains(t.Recipes, r => r.Activate);
            Assert.All(t.Flows.SelectMany(f => f.Edges.Select(e => (f, e))), x =>
            {
                Assert.Contains(x.f.Nodes, n => n.Id == x.e.From);
                Assert.Contains(x.f.Nodes, n => n.Id == x.e.To);
            });
        });
    }

    [Fact]
    public async Task TrayAssembly_BindsCells_RejectsNg_AndCompletesFullTray()
    {
        await using var m = await StartAsync(MachineTypes.TrayAssembly, t =>
        {
            t.Recipes[0].Params["TrayRows"] = 1;
            t.Recipes[0].Params["TrayCols"] = 3;
        });

        Assert.Equal(1, await m.CycleAsync("M10", "M11", "D30", ("D300", "T0001")));
        Assert.Equal(2, await m.CycleAsync("M10", "M11", "D30", ("D300", "bad")));

        (string, object)[] Cell(string code, short pos, float v = 3.30f, float ir = 0.25f) =>
            [("D100", code), ("D12", pos), ("D14", v), ("D16", ir)];

        Assert.Equal(1, await m.CycleAsync("M0", "M1", "D10", Cell("CELL0000000001", 1)));
        Assert.Equal((short)1, SimulatorMemory.Read(m.Sim, "D32"));
        Assert.Equal(2, await m.CycleAsync("M0", "M1", "D10", Cell("CELL0000000001", 2)));
        Assert.Equal(2, await m.CycleAsync("M0", "M1", "D10", Cell("CELL0000000009", 2, v: 2.5f)));
        Assert.Equal(1, await m.CycleAsync("M0", "M1", "D10", Cell("CELL0000000002", 2)));
        Assert.Equal(false, SimulatorMemory.Read(m.Sim, "M12"));
        Assert.Equal(1, await m.CycleAsync("M0", "M1", "D10", Cell("CELL0000000003", 3)));
        Assert.Equal(true, SimulatorMemory.Read(m.Sim, "M12"));
        Assert.Equal(2, await m.CycleAsync("M0", "M1", "D10", Cell("CELL0000000004", 1)));

        var trays = m.Host.Get<ITrayService>();
        Assert.Equal(TrayStatus.Uploaded, (await trays.GetTrayAsync("T0001"))!.Status);
        Assert.Equal(["CELL0000000001", "CELL0000000002", "CELL0000000003"], (await trays.GetCellsAsync("T0001")).OrderBy(c => c.Position).Select(c => c.CellCode));

        Assert.Equal(3, m.Records.Count(r => r.IsOk));
        Assert.All(m.Records, r => Assert.Equal(MachineTypes.TrayAssembly, r.MachineType));
        Assert.Equal(["NG_BARCODE", "NG_SPEC", "NO_TRAY"], (await m.NgRecordsAsync()).Select(r => r.NgCode));
    }

    [Fact]
    public async Task TrayDisassembly_UnbindsByPosition_AndSignalsEmpty()
    {
        await using var m = await StartAsync(MachineTypes.TrayDisassembly);
        var trays = m.Host.Get<ITrayService>();
        await trays.GetOrCreateTrayAsync("T0100", 1, 3);
        Assert.True((await trays.BindCellAsync("T0100", 1, "CELLA000000001")).Success);
        Assert.True((await trays.BindCellAsync("T0100", 2, "CELLA000000002")).Success);

        Assert.Equal(1, await m.CycleAsync("M10", "M11", "D30", ("D300", "T0100")));
        Assert.Equal((short)2, SimulatorMemory.Read(m.Sim, "D32"));

        Assert.Equal(1, await m.CycleAsync("M0", "M1", "D10", ("D12", (short)1), ("D100", "CELLA000000001")));
        Assert.Equal(2, await m.CycleAsync("M0", "M1", "D10", ("D12", (short)3), ("D100", "CELLA000000003")));
        Assert.Equal(false, SimulatorMemory.Read(m.Sim, "M12"));
        Assert.Equal(1, await m.CycleAsync("M0", "M1", "D10", ("D12", (short)2), ("D100", "CELLA000000002")));
        Assert.Equal(true, SimulatorMemory.Read(m.Sim, "M12"));
        Assert.Empty(await trays.GetCellsAsync("T0100"));

        Assert.Equal(2, await m.CycleAsync("M10", "M11", "D30", ("D300", "T0999")));
        Assert.Contains(m.Host.Get<IAlarmService>().ActiveAlarms, a => a.Source == "TrayEmptyIn");

        Assert.Equal([1, 2], m.Records.Where(r => r.IsOk).Select(r => r.Position ?? 0).Order());
        Assert.Equal(["NG_POSITION"], (await m.NgRecordsAsync()).Select(r => r.NgCode));
    }

    [Fact]
    public async Task Grading_AssignsBinsByRecipeRules()
    {
        await using var m = await StartAsync(MachineTypes.Grading);

        Task<int> Grade(string code, float v, float ir, float cap) =>
            m.CycleAsync("M0", "M1", "D10", ("D100", code), ("D14", v), ("D16", ir), ("D18", cap));

        Assert.Equal(1, await Grade("CELLG000000001", 3.29f, 0.18f, 285f));
        Assert.Equal(2, await Grade("CELLG000000002", 3.27f, 0.21f, 282f));
        Assert.Equal(4, await Grade("CELLG000000003", 3.21f, 0.28f, 272f));
        Assert.Equal(7, await Grade("CELLG000000004", 3.10f, 0.18f, 285f));
        Assert.Equal(8, await Grade("CELLG000000005", 3.29f, 0.50f, 285f));
        Assert.Equal(8, await Grade("CELLG000000001", 3.29f, 0.18f, 285f));

        Assert.Equal([1, 2, 4, 7, 8, 8], m.Records.Select(r => r.BinNo ?? 0));
        Assert.Equal([true, true, true, false, false, false], m.Records.Select(r => r.IsOk));
        Assert.Equal(["NG_V_LOW", "NG_GRADE", "NG_BARCODE"], (await m.NgRecordsAsync()).Select(r => r.NgCode));
        Assert.Equal(1, m.Host.Get<IProductionService>().Counters.BinCounts.GetValueOrDefault(4));
    }

    [Fact]
    public async Task NgSorting_MapsNgCodesToBoxes_AndHandlesBoxFull()
    {
        await using var m = await StartAsync(MachineTypes.NgSorting);

        Task<int> Sort(string code, float v, float ir) =>
            m.CycleAsync("M0", "M1", "D10", ("D100", code), ("D14", v), ("D16", ir));

        Assert.Equal(0, await Sort("CELLN000000001", 3.30f, 0.20f));
        Assert.Equal(1, await Sort("CELLN000000002", 3.10f, 0.20f));
        Assert.Equal(1, await Sort("CELLN000000003", 3.70f, 0.20f));
        Assert.Equal(2, await Sort("CELLN000000004", 3.30f, 0.40f));
        Assert.Equal(4, await Sort("BAD", 3.30f, 0.20f));

        var ng = await m.NgRecordsAsync();
        Assert.Equal(["NG_V_LOW", "NG_V_HIGH", "NG_IR", "NG_BARCODE"], ng.Select(r => r.NgCode));
        Assert.Equal([1, 1, 2, 4], ng.Select(r => r.BoxNo));
        Assert.Single(m.Records, r => r.IsOk);

        var alarms = m.Host.Get<IAlarmService>();
        SimulatorMemory.Set(m.Sim, "M20", true);
        Assert.True(await m.Host.WaitUntilAsync(() => alarms.ActiveAlarms.Any(a => a.Source == "NgBoxFull")), "料盒满未报警");
        SimulatorMemory.Set(m.Sim, "M20", false);
        Assert.True(await m.Host.WaitUntilAsync(() => alarms.ActiveAlarms.All(a => a.Source != "NgBoxFull")), "料盒满报警未自动清除");
    }
}
