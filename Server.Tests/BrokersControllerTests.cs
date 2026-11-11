using InvestmentTracker.Server.Controllers;
using InvestmentTracker.Server.Data;
using InvestmentTracker.Server.Models;
using InvestmentTracker.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace InvestmentTracker.Server.Tests;

public sealed class BrokersControllerTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ApplicationDbContext _context;
    private readonly BrokersController _controller;

    public BrokersControllerTests()
    {
        _connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _context = new ApplicationDbContext(options);
        _context.Database.EnsureCreated();
        _controller = new BrokersController(_context);
    }

    [Fact]
    public async Task GetAll_ReturnsBrokerDtos()
    {
        _context.Brokers.AddRange(
            new Broker { Name = "Broker A", SiteUrl = "https://a.example", DefaultCommissionRate = 0.1m, IsApproved = true },
            new Broker { Name = "Broker B", DefaultCommissionRate = 0.2m });
        await _context.SaveChangesAsync();

        var actionResult = await _controller.GetAll();
        var result = Assert.IsType<OkObjectResult>(actionResult.Result);
        var brokers = Assert.IsType<List<BrokerDto>>(result.Value);

        Assert.Equal(2, brokers.Count);
        Assert.Contains(brokers, b => b.Name == "Broker A" && b.SiteUrl == "https://a.example" && b.DefaultCommissionRate == 0.1m && b.IsApproved);
        Assert.Contains(brokers, b => b.Name == "Broker B" && !b.IsApproved);
    }

    [Fact]
    public async Task GetById_WhenBrokerExists_ReturnsBrokerDto()
    {
        var broker = new Broker { Name = "Broker A", SiteUrl = "https://a.example", DefaultCommissionRate = 0.1m, IsApproved = true };
        _context.Brokers.Add(broker);
        await _context.SaveChangesAsync();

        var actionResult = await _controller.GetById(broker.Id);
        var result = Assert.IsType<OkObjectResult>(actionResult.Result);
        var dto = Assert.IsType<BrokerDto>(result.Value);

        Assert.Equal(broker.Id, dto.Id);
        Assert.Equal(broker.Name, dto.Name);
        Assert.Equal(broker.SiteUrl, dto.SiteUrl);
        Assert.Equal(broker.DefaultCommissionRate, dto.DefaultCommissionRate);
        Assert.True(dto.IsApproved);
    }

    [Fact]
    public async Task GetById_WhenBrokerDoesNotExist_ReturnsNotFound()
    {
        var result = await _controller.GetById(999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task Create_SavesBrokerAsUnapproved_AndReturnsCreatedLocation()
    {
        var dto = new BrokerDto { Name = "New Broker", SiteUrl = "https://new.example", DefaultCommissionRate = 0.15m, IsApproved = true };

        var actionResult = await _controller.Create(dto);
        var result = Assert.IsType<CreatedAtActionResult>(actionResult.Result);
        var saved = await _context.Brokers.SingleAsync();

        Assert.Equal(nameof(BrokersController.GetById), result.ActionName);
        Assert.Equal(saved.Id, result.RouteValues!["id"]);
        Assert.Equal(saved.Id, dto.Id);
        Assert.False(dto.IsApproved);
        Assert.Equal("New Broker", saved.Name);
        Assert.Equal("https://new.example", saved.SiteUrl);
        Assert.Equal(0.15m, saved.DefaultCommissionRate);
        Assert.False(saved.IsApproved);
    }

    [Fact]
    public async Task Update_WhenBrokerExists_UpdatesEditableFieldsAndPreservesApproval()
    {
        var broker = new Broker { Name = "Old", SiteUrl = "https://old.example", DefaultCommissionRate = 0.1m, IsApproved = true };
        _context.Brokers.Add(broker);
        await _context.SaveChangesAsync();

        var result = await _controller.Update(broker.Id, new BrokerDto
        {
            Name = "Updated", SiteUrl = "https://updated.example", DefaultCommissionRate = 0.25m, IsApproved = false
        });

        Assert.IsType<NoContentResult>(result);
        var saved = await _context.Brokers.FindAsync(broker.Id);
        Assert.NotNull(saved);
        Assert.Equal("Updated", saved.Name);
        Assert.Equal("https://updated.example", saved.SiteUrl);
        Assert.Equal(0.25m, saved.DefaultCommissionRate);
        Assert.True(saved.IsApproved);
    }

    [Fact]
    public async Task Update_WhenBrokerDoesNotExist_ReturnsNotFound()
    {
        var result = await _controller.Update(999, new BrokerDto { Name = "Missing" });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task Delete_WhenBrokerExists_RemovesBroker()
    {
        var broker = new Broker { Name = "To delete" };
        _context.Brokers.Add(broker);
        await _context.SaveChangesAsync();

        var result = await _controller.Delete(broker.Id);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(await _context.Brokers.ToListAsync());
    }

    [Fact]
    public async Task Delete_WhenBrokerDoesNotExist_ReturnsNotFound()
    {
        var result = await _controller.Delete(999);

        Assert.IsType<NotFoundResult>(result);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }
}
