namespace WorkflowApi.Kycs;

public sealed class KycResult
{
    public string? Agent { get; set; }
    public string? Status { get; set; }
    public string? Notes { get; set; }
}