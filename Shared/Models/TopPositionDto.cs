public class TopPositionDto
{
    public string Ticker { get; set; } = string.Empty;
    public string SecurityName { get; set; } = string.Empty;  // <-- новое поле
    public decimal CurrentPrice { get; set; }
    public decimal? ChangePercent { get; set; }
    public decimal TotalValue { get; set; }
}