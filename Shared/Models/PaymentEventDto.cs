namespace InvestmentTracker.Shared.Models
{
    public class PaymentEventDto
    {
        public int Id { get; set; }
        public string Ticker { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public decimal AmountPerUnit { get; set; }
        public string Currency { get; set; } = "RUB";
        public string Type { get; set; } = "Dividend";
        public decimal? UserQuantity { get; set; }       // null для гостя
        public decimal? UserTotalAmount { get; set; }    // null для гостя
    }
}