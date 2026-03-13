using System.ComponentModel;
using System.Text.Json;

namespace WorkflowApi.Incomes;

public static class IncomeTools
{
    [Description("Assess income capacity for a credit application")]
    public static async Task<string> IncomeTool(
        [Description("The credit application JSON payload")] string applicationJson)
    {
        //await Task.Delay(TimeSpan.FromSeconds(15));
        try
        {
            using var doc = JsonDocument.Parse(applicationJson);
            if (doc.RootElement.TryGetProperty("amount", out var amountProp) &&
                amountProp.TryGetDecimal(out var amount))
            {
                return amount <= 75_000m ? "Sufficient" : "Insufficient";
            }
        }
        catch
        {
            // Fall through to Review.
        }

        return "Review";
    }

}