using System.ComponentModel.DataAnnotations;

namespace InvestmentTracker.Shared.Models
{
    public class ProfileDto
    {
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public DateTime? LastLoginDate { get; set; }
    }

    public class ChangePasswordDto
    {
        [Required]
        public string OldPassword { get; set; } = string.Empty;

        [Required]
        //[MinLength(6)]
        public string NewPassword { get; set; } = string.Empty;
    }
}