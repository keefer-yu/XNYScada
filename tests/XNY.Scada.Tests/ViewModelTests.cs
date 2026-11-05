using Microsoft.Extensions.DependencyInjection;
using XNY.Scada.Application.Flows;
using XNY.Scada.Presentation;
using XNY.Scada.Presentation.Services;
using XNY.Scada.Presentation.ViewModels;
using Xunit;

namespace XNY.Scada.Tests;

/// <summary>模拟数据对话框。</summary>
public sealed class FakeDialogs : IDialogService
{
    /// <summary>错误。</summary>
    public List<string> Errors { get; } = [];
    /// <summary>提示应答。</summary>
    public Queue<string?> PromptAnswers { get; } = new();

    /// <summary>显示信息。</summary>
    public void ShowInfo(string message, string title = "提示") { }
    /// <summary>显示错误。</summary>
    public void ShowError(string message, string title = "错误") => Errors.Add(message);
    /// <summary>确认。</summary>
    public bool Confirm(string message, string title = "确认") => true;
    /// <summary>提示。</summary>
    public string? Prompt(string message, string title, string defaultValue = "") => PromptAnswers.Count > 0 ? PromptAnswers.Dequeue() : defaultValue;
    /// <summary>请求签名。</summary>
    public SignatureCredential? RequestSignature(string reason) => new("admin", "Admin@123", "test");
    /// <summary>请求密码更改。</summary>
    public PasswordChange? RequestPasswordChange() => null;
    /// <summary>打开文件。</summary>
    public string? OpenFile(string filter) => null;
    /// <summary>保存文件。</summary>
    public string? SaveFile(string filter, string defaultName) => null;
}

/// <summary>视图模型测试。</summary>
public class ViewModelTests
{
    /// <summary>测试起点。</summary>
    private static Task<TestHost> StartAsync(FakeDialogs dialogs) => TestHost.StartAsync(configure: s =>
    {
        s.AddScadaPresentation();
        s.AddSingleton<IDialogService>(dialogs);
        s.AddSingleton<IUiDispatcher, ImmediateDispatcher>();
    });

    /// <summary>测试全部页面激活不含错误。</summary>
    [Fact]
    public async Task AllPages_ActivateWithoutErrors()
    {
        var dialogs = new FakeDialogs();
        await using var h = await StartAsync(dialogs);
        var shell = h.Get<ShellViewModel>();
        Assert.True(shell.IsLoggedIn);
        Assert.All(shell.MenuItems, m => Assert.True(m.IsVisible, m.Key));
        foreach (var item in shell.MenuItems)
        {
            var page = (ViewModelBase)h.Services.GetRequiredService(item.ViewModelType);
            await page.ActivateAsync();
            page.Deactivate();
            Assert.True(dialogs.Errors.Count == 0, $"{item.Key}: {string.Join(";", dialogs.Errors)}");
        }
    }

    /// <summary>测试流程设计器构建保存发布并模拟。</summary>
    [Fact]
    public async Task FlowDesigner_BuildSavePublishAndSimulate()
    {
        var dialogs = new FakeDialogs();
        await using var h = await StartAsync(dialogs);
        var vm = h.Get<FlowDesignerViewModel>();
        await vm.ActivateAsync();

        dialogs.PromptAnswers.Enqueue("VmFlow");
        await vm.NewFlowCommand.ExecuteAsync(null);
        Assert.True(vm.IsLoaded);
        var start = Assert.Single(vm.Nodes);

        var calc = vm.AddNode("SetVariable", 300, 160)!;
        calc.Properties.First(p => p.Key == "Assignments").Value = "X = 20 + 22";
        var end = vm.AddNode("End", 300, 300)!;
        vm.Connect(start, "next", calc);
        vm.Connect(calc, "next", end);
        Assert.Equal(2, vm.Edges.Count);
        Assert.True(vm.Edges[0].Y1 > 0 && vm.Edges[0].PathData.StartsWith("M "));

        await vm.ValidateCommand.ExecuteAsync(null);
        Assert.DoesNotContain(vm.Issues, i => i.IsError);

        dialogs.PromptAnswers.Enqueue("first");
        await vm.PublishCommand.ExecuteAsync(null);
        Assert.Empty(dialogs.Errors);
        var def = (await h.Get<FlowDefinitionService>().ListAsync()).Single(f => f.Code == "VmFlow");
        Assert.Equal(1, def.PublishedVersion);
        Assert.False(vm.IsDirty);

        await vm.SimulateCommand.ExecuteAsync(null);
        Assert.Empty(dialogs.Errors);
        Assert.StartsWith("Completed", vm.SimulationResult);
        Assert.Contains(vm.SimulationVariables, kv => kv.Key == "X" && kv.Value == "42");
        Assert.Equal(NodeExecState.Done, calc.ExecState);

        // 撤销删除
        vm.SelectedNode = calc;
        vm.DeleteSelected();
        Assert.Equal(2, vm.Nodes.Count);
        await vm.UndoCommand.ExecuteAsync(null);
        Assert.Equal(3, vm.Nodes.Count);
        Assert.Equal(2, vm.Edges.Count);
    }

    /// <summary>测试分档规则行解析并格式条件。</summary>
    [Fact]
    public void GradingRuleRow_ParsesAndFormatsConditions()
    {
        var list = GradingRuleRow.ParseConditions("Voltage:3.2~3.3; InternalResistance:~0.5；Capacity：100~");
        Assert.Equal(3, list.Count);
        Assert.Equal(3.2, list[0].Min);
        Assert.Null(list[1].Min);
        Assert.Equal(0.5, list[1].Max);
        Assert.Equal(100, list[2].Min);
        Assert.Null(list[2].Max);
        Assert.Equal("Voltage:3.2~3.3; InternalResistance:~0.5; Capacity:100~", GradingRuleRow.FormatConditions(list));
        Assert.Throws<FormatException>(() => GradingRuleRow.ParseConditions("Voltage=3"));
    }
}
