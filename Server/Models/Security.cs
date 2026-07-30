using System.ComponentModel.DataAnnotations;

namespace InvestmentTracker.Server.Models
{
    public class Security
    {
        public int Id { get; set; }

        [Required, MaxLength(20)]
        public string Ticker { get; set; } = string.Empty;

        [MaxLength(12)]
        public string? Isin { get; set; }

        [Required, MaxLength(500)]
        public string Name { get; set; } = string.Empty;

        public int AssetTypeId { get; set; }
        public AssetType AssetType { get; set; } = null!;

        // Новые поля
        public DateTime? NextCouponDate { get; set; }       // ближайшая дата купона
        public decimal? AccruedInterest { get; set; }        // НКД в рублях
        public long? IssueSize { get; set; }                 // количество бумаг в выпуске
        public decimal? FaceValue { get; set; }              // номинал одной бумаги
        [MaxLength(20)]
        public string? Rating { get; set; }                  // рейтинг (пока вручную)

        public ICollection<PortfolioItem> PortfolioItems { get; set; } = new List<PortfolioItem>();
    }
}