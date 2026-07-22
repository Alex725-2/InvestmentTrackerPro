using System.ComponentModel.DataAnnotations;

namespace InvestmentTracker.Server.Models
{
    public class PaymentEvent
    {
        public int Id { get; set; }

        [Required, MaxLength(20)]
        public string Ticker { get; set; } = string.Empty;

        public DateTime Date { get; set; }

        [DataType("decimal(18,4)")]
        public decimal AmountPerUnit { get; set; }

        [MaxLength(10)]
        public string Currency { get; set; } = "RUB";

        [MaxLength(20)]
        public string Type { get; set; } = "Dividend"; // Dividend, Coupon, Amortization

        public int SecurityId { get; set; }
        public Security Security { get; set; } = null!;

        public bool IsEstimated { get; set; } // true – прогноз, false – гарантировано
    }
}