using Microsoft.Extensions.DependencyInjection;
using XNY.Scada.Application.Flows;
using XNY.Scada.Presentation;
using XNY.Scada.Presentation.Services;
using XNY.Scada.Presentation.ViewModels;
using Xunit;

namespace XNY.Scada.Tests;

public sealed class FakeDialogs : IDialogService
{
    public List<string> Errors { get; } = [];
    public Queue<string?> PromptAnswers { get; } = new();

    public void ShowInfo(string message, string title = "提示") { }
    public void ShowError(string message, string title = "错误") => Errors.Add(message);
    public bool Confirm(string message, string title = "确认") => true;
    public string? Prompt(string message, string title, string defaultValue = "") => PromptAnswers.Count > 0 ? PromptAnswers.Dequeue() : defaultValue;
    public SignatureCredential? RequestSignature(string reason) => new("admin", "Admin@123", "test");
    public PasswordChange? RequestPasswordChange() => null;
    public string? OpenFile(string filter) => null;
    public string? SaveFile(string filter, string defaultName) => null;
}

public class ViewModelTests
{
    private static Task<TestHost> StartAsync(FakeDialogs dialogs) => TestHost.StartAsync(configure: s =>
    {
        s.AddScadaPresentation();
        s.AddSingleton<IDialogService>(dialogs);
        s.AddSingleton<IUiDispatcher, ImmediateDispatcher>();
    });

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
