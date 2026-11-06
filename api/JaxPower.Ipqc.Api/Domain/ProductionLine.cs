namespace JaxPower.Ipqc.Api.Domain;

public sealed class ProductionLine
{
    public Guid Id { get; set; }
    private string code = "";
    public string Code
    {
        get => code;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("Line code is required.", nameof(value));
            code = value.Trim();
            NormalizedCode = code.ToUpperInvariant();
        }
    }
    public string NormalizedCode { get; private set; } = "";
    public string Name { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
