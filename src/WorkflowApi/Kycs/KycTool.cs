using System.ComponentModel;

namespace WorkflowApi.Kycs;

public static class KycTools
{
    [Description("Know Your Customer by by CPF number")]
    public static async Task<string> ValidateCpf(
        [Description("The CPF formated or unformatted")]
        string cpf)
    {
        //await Task.Delay(TimeSpan.FromSeconds(5));
        return cpf == "123.456.789-00" ? "Approved" : "Rejected";
    }
}