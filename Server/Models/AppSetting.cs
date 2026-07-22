using System.ComponentModel.DataAnnotations;

namespace InvestmentTracker.Server.Models
{
    public class AppSetting
    {
        public int Id { get; set; }

        [Required, MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(100)]
        public string Code { get; set; } = string.Empty; // уникальный ключ

        public bool Enabled { get; set; }
    }
}