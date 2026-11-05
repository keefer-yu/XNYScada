using XNY.Scada.Application.Production;
using XNY.Scada.Domain;
using XNY.Scada.Domain.Entities;
using XNY.Scada.Domain.Expressions;
using XNY.Scada.Mes;
using XNY.Scada.Security;
using Xunit;

namespace XNY.Scada.Tests;

public class CoreTests
{
    [Fact]
    public void PasswordHasher_RoundTrip()
    {
        var hash = PasswordHasher.Hash("Abc12345");
        Assert.True(PasswordHasher.Verify("Abc12345", hash));
        Assert.False(PasswordHasher.Verify("abc12345", hash));
    }

    [Fact]
    public void Expression_SupportsVariablesPlaceholdersAndFunctions()
    {
        var vars = new Dictionary<string, object?> { ["Voltage"] = 3.25f, ["Code"] = "CELL001" };
        Assert.True(ScadaExpression.EvaluateBool("Voltage > 3.2 && Voltage < 3.3", vars));
        Assert.True(ScadaExpression.EvaluateBool("startsWith(Code, 'CELL') && len(Code) == 7", vars));
        Assert.Equal(5L, Convert.ToInt64(ScadaExpression.Evaluate("{Tag:PLC.A} + 2", vars, (k, n) => 3)));
        Assert.Equal("cell=CELL001 bin=2", ScadaExpression.Render("cell=${Code} bin={Var:Bin}", vars, (k, n) => 2));
    }

    [Fact]
    public void Grading_MatchesConditionsAndExpression()
    {
        var rule = new GradingRuleEntity { BinNo = 1, Expression = "Capacity >= 50" };
        var conditions = new List<GradingCondition> { new() { Field = "Voltage", Min = 3.2, Max = 3.3 } };
        Assert.True(GradingService.Match(rule, conditions, new Dictionary<string, object?> { ["Voltage"] = 3.25, ["Capacity"] = 51 }));
        Assert.False(GradingService.Match(rule, conditions, new Dictionary<string, object?> { ["Voltage"] = 3.35, ["Capacity"] = 51 }));
        Assert.False(GradingService.Match(rule, conditions, new Dictionary<string, object?> { ["Voltage"] = 3.25, ["Capacity"] = 40 }));
    }

    [Fact]
    public void Mes_RenderAndInterpret()
    {
        var body = MesClient.RenderBody("{\"sn\":\"${Sn}\",\"v\":${V}}", new Dictionary<string, object?> { ["Sn"] = "A\"1", ["V"] = 3.5 });
        Assert.Equal("{\"sn\":\"A\\u00221\",\"v\":3.5}", body);

        var cfg = new MesInterfaceEntity { ResponseMapJson = "{\"Code\":\"code\",\"Bin\":\"data.items[0].bin\"}", SuccessExpression = "Code == 0" };
        var r = MesClient.Interpret(cfg, 200, "{\"code\":0,\"data\":{\"items\":[{\"bin\":3}]}}");
        Assert.True(r.Success);
        Assert.Equal(3L, r.Fields["Bin"]);
        Assert.False(MesClient.Interpret(cfg, 200, "{\"code\":1}").Success);
    }

    [Fact]
    public void Json_RoundTripsEnums()
    {
        var json = Json.Serialize(new { Kind = AlarmKind.High });
        Assert.Contains("High", json);
    }
}
