using Microsoft.AspNetCore.Identity;
using InvestmentTracker.Server.Models;
using InvestmentTracker.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace InvestmentTracker.Server.Data
{
    public static class SeedData
    {
        public static async Task Initialize(IServiceProvider serviceProvider)
        {
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();

            // Создаём роль Admin, если её нет
            if (!await roleManager.RoleExistsAsync("Admin"))
            {
                await roleManager.CreateAsync(new IdentityRole("Admin"));
            }

            if (!context.AppSettings.Any())
            {
                context.AppSettings.Add(new AppSetting
                {
                    Name = "Отправка уведомлений администратору о новых пользователях",
                    Code = "SendNotificationAboutNewUser",
                    Enabled = true
                });
                await context.SaveChangesAsync();
            }

            // 1. Главный администратор
            var adminEmail = "admin@example.com";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser == null)
            {
                adminUser = new ApplicationUser
                {
                    UserName = "admin",
                    Email = adminEmail,
                    FullName = "Admin User"
                };
                var result = await userManager.CreateAsync(adminUser, "Admin123!"); // потом сменим
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                }
            }

            // 2. Наставник (для аудита)
            var mentorEmail = "mentor@test.com";
            var mentorUser = await userManager.FindByEmailAsync(mentorEmail);
            if (mentorUser == null)
            {
                mentorUser = new ApplicationUser
                {
                    UserName = "mentor",
                    Email = mentorEmail,
                    FullName = "Mentor"
                };
                var result = await userManager.CreateAsync(mentorUser, "dfhsjkd324#76##FQ");
                if (result.Succeeded)
                {
                    await userManager.AddToRoleAsync(mentorUser, "Admin");
                }
            }

            // Валюты
            if (!context.Currencies.Any())
            {
                context.Currencies.AddRange(
                    new Currency { Code = "RUR", Name = "Российский рубль" },
                    new Currency { Code = "USD", Name = "Доллар США" },
                    new Currency { Code = "EUR", Name = "Евро" },
                    new Currency { Code = "CNY", Name = "Юань" }
                );
                await context.SaveChangesAsync();
            }

            // Типы активов
            if (!context.AssetTypes.Any())
            {
                context.AssetTypes.AddRange(
                    new AssetType { Name = "Акция" },
                    new AssetType { Name = "Облигация" },
                    new AssetType { Name = "ПИФ" },
                    new AssetType { Name = "ETF" }
                );
                await context.SaveChangesAsync();
            }

            if (!context.TestRecords.Any())
            {
                context.TestRecords.Add(new TestRecord { Name = "Test OK" });
                await context.SaveChangesAsync();
            }
        }
    }
}