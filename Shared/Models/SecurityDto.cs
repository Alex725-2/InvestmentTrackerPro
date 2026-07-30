namespace InvestmentTracker.Shared.Models
{
    public class SecurityDto
    {
        public int Id { get; set; }
        public string Ticker { get; set; } = string.Empty;
        public string? Isin { get; set; }
        public string Name { get; set; } = string.Empty;
        public int AssetTypeId { get; set; }
        public string? AssetTypeName { get; set; }

        // Новые поля
        public DateTime? NextCouponDate { get; set; }
        public decimal? AccruedInterest { get; set; }
        public long? IssueSize { get; set; }
        public decimal? FaceValue { get; set; }
        public string? Rating { get; set; }
    }
}