using XNY.Scada.Application.Devices;
using XNY.Scada.Application.Flows;
using XNY.Scada.Device.Runtime;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Flow.Model;
using XNY.Scada.Flow.Runtime;
using Xunit;

namespace XNY.Scada.Tests;

public class FlowEngineTests
{
    private static async Task SetupDeviceAsync(TestHost h, string instance)
    {
        var devices = h.Get<DeviceConfigService>();
        var dev = await devices.SaveDeviceAsync(new DeviceEntity { Name = "PLC", DriverKey = "Simulator", OptionsJson = $"{{\"Instance\":\"{instance}\"}}" });
        var g = (await devices.GetGroupsAsync(dev.Id))[0];
        g.ScanIntervalMs = 20;
        await devices.SaveGroupAsync(g);
        await devices.SaveTagsAsync(dev.Id,
        [
            new TagEntity { Name = "Req", Address = "M0", DataType = TagDataType.Bool },
            new TagEntity { Name = "Done", Address = "M1", DataType = TagDataType.Bool },
            new TagEntity { Name = "Value", Address = "D0", DataType = TagDataType.Int16 },
            new TagEntity { Name = "Result", Address = "D2", DataType = TagDataType.Int16 },
            new TagEntity { Name = "Temp", Address = "D4", DataType = TagDataType.Int16, Scale = 0.1 }
        ]);
        await devices.ApplyAsync();
    }

    private static FlowDocument DoubleFlow() => new()
    {
        Code = "Double",
        Name = "乘二",
        StartNodeId = "read",
        Triggers = [new TriggerDef { Type = TriggerType.TagRisingEdge, Tag = "PLC.Req" }],
        Nodes =
        [
            new NodeDef { Id = "read", Type = "ReadTag", Properties = { ["Tag"] = "PLC.Value", ["Variable"] = "V" } },
            new NodeDef { Id = "cond", Type = "Condition", Properties = { ["Expression"] = "V > 0" } },
            new NodeDef { Id = "calc", Type = "SetVariable", Properties = { ["Assignments"] = "R = V * 2" } },
            new NodeDef { Id = "neg", Type = "SetVariable", Properties = { ["Assignments"] = "R = -1" } },
            new NodeDef { Id = "hs", Type = "Handshake", Properties = { ["RequestTag"] = "PLC.Req", ["DoneTag"] = "PLC.Done", ["ResultTag"] = "PLC.Result", ["Result"] = "R", ["Timeout"] = "3000" } }
        ],
        Edges =
        [
            new EdgeDef { From = "read", To = "cond" },
            new EdgeDef { From = "cond", Port = "true", To = "calc" },
            new EdgeDef { From = "cond", Port = "false", To = "neg" },
            new EdgeDef { From = "calc", To = "hs" },
            new EdgeDef { From = "neg", To = "hs" }
        ]
    };

    [Fact]
    public async Task TagTriggeredFlow_RunsHandshakeAndWritesResult()
    {
        await using var h = await TestHost.StartAsync();
        var sim = Guid.NewGuid().ToString("N");
        await SetupDeviceAsync(h, sim);
        var flows = h.Get<FlowDefinitionService>();
        var v = await flows.SaveDraftAsync(DoubleFlow());
        await flows.PublishAsync("Double", v, null, "test");

        var completed = new TaskCompletionSource<FlowInstance>();
        h.Get<IFlowEngine>().InstanceCompleted += (_, i) => completed.TrySetResult(i);

        SimulatorMemory.Set(sim, "D0", (short)21);
        SimulatorMemory.Set(sim, "M0", true);
        Assert.True(await h.WaitUntilAsync(() => Equals(h.Tag("PLC.Done"), true)), "完成位未置位");
        Assert.Equal((short)42, SimulatorMemory.Read(sim, "D2"));
        SimulatorMemory.Set(sim, "M0", false);

        var instance = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(FlowInstanceStatus.Completed, instance.Status);
        Assert.True(await h.WaitUntilAsync(() => Equals(SimulatorMemory.Read(sim, "M1"), false)));
        Assert.Equal(["read", "cond", "calc", "hs"], instance.Trace.Select(t => t.NodeId));
    }

    [Fact]
    public async Task Publish_NewVersion_HotSwapsWhenIdle_AndScalingApplies()
    {
        await using var h = await TestHost.StartAsync();
        var sim = Guid.NewGuid().ToString("N");
        await SetupDeviceAsync(h, sim);
        var flows = h.Get<FlowDefinitionService>();
        var engine = h.Get<IFlowEngine>();

        var doc = DoubleFlow();
        doc.Triggers.Clear();
        await flows.PublishAsync("Double", await flows.SaveDraftAsync(doc), null, null);
        Assert.Equal(1, engine.GetFlows().Single().Version);

        doc.Nodes.Single(n => n.Id == "calc").Properties["Assignments"] = "R = V * 3";
        var v2 = await flows.SaveDraftAsync(doc);
        Assert.Equal(2, v2);
        await flows.PublishAsync("Double", v2, null, null);
        Assert.Equal(2, engine.GetFlows().Single().Version);

        var write = await h.Get<Device.ITagService>().WriteAsync("PLC.Temp", 36.5);
        Assert.True(write.IsSuccess);
        Assert.Equal((short)365, SimulatorMemory.Read(sim, "D4"));
        Assert.True(await h.WaitUntilAsync(() => Equals(h.Tag("PLC.Temp"), 36.5)));
    }

    [Fact]
    public async Task Validator_ReportsMissingPropertiesAndUnknownNodes()
    {
        await using var h = await TestHost.StartAsync();
        var flows = h.Get<FlowDefinitionService>();
        var doc = new FlowDocument
        {
            Code = "Bad",
            StartNodeId = "a",
            Nodes = [new NodeDef { Id = "a", Type = "WriteTag" }, new NodeDef { Id = "b", Type = "NoSuchNode" }]
        };
        var issues = flows.Validate(doc);
        Assert.Contains(issues, i => i.IsError && i.Message.Contains("缺少必填属性"));
        Assert.Contains(issues, i => i.IsError && i.Message.Contains("未知节点类型"));
        Assert.Contains(issues, i => !i.IsError && i.Message.Contains("不可达"));
        var v = await flows.SaveDraftAsync(doc);
        await Assert.ThrowsAsync<XNY.Scada.Flow.FlowException>(() => flows.PublishAsync("Bad", v, null, null));
    }

    [Fact]
    public async Task ScriptNode_And_LoopNode_Work_InSimulation()
    {
        await using var h = await TestHost.StartAsync();
        var doc = new FlowDocument
        {
            Code = "Loop",
            StartNodeId = "init",
            Nodes =
            [
                new NodeDef { Id = "init", Type = "SetVariable", Properties = { ["Assignments"] = "Sum = 0" } },
                new NodeDef { Id = "loop", Type = "Loop", Properties = { ["Count"] = "4", ["Counter"] = "I" } },
                new NodeDef { Id = "add", Type = "SetVariable", Properties = { ["Assignments"] = "Sum = Sum + I" } },
                new NodeDef { Id = "script", Type = "Script", Properties = { ["Code"] = "Vars[\"Text\"] = \"S\" + Num(\"Sum\"); return Num(\"Sum\") == 6 ? \"six\" : null;" } },
                new NodeDef { Id = "end", Type = "End" }
            ],
            Edges =
            [
                new EdgeDef { From = "init", To = "loop" },
                new EdgeDef { From = "loop", Port = "body", To = "add" },
                new EdgeDef { From = "add", To = "loop" },
                new EdgeDef { From = "loop", Port = "done", To = "script" },
                new EdgeDef { From = "script", Port = "six", To = "end" }
            ]
        };
        var instance = await h.Get<IFlowEngine>().RunDocumentAsync(doc);
        Assert.Equal(FlowInstanceStatus.Completed, instance.Status);
        Assert.Equal(6L, Convert.ToInt64(instance.Variables["Sum"]));
        Assert.Equal("S6", instance.Variables["Text"]);
        Assert.Equal("end", instance.Trace.Last().NodeId);
    }
}
