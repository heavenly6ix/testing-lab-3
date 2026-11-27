using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Shop.Orders;

namespace Part1.Orders.Tests;

[TestFixture]
public class ReserveInventoryTests
{
    private Mock<IOrderRepository> orders = null!;
    private Mock<IInventoryService> inventory = null!;
    private Mock<IPaymentGateway> payments = null!;
    private Mock<INotificationService> notifications = null!;
    private Mock<IDiscountEngine> discounts = null!;
    private Mock<ITaxCalculator> tax = null!;
    private Mock<IUnitOfWork> uow = null!;
    private Mock<IClock> clock = null!;
    private Mock<IDelayer> delayer = null!;
    private Mock<ILogger<OrderProcessingService>> logger = null!;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);
    private static TaskCompletionSource<T> Gate<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static OrderLine[] Lines() => [new("A", 1, 10m), new("B", 2, 10m), new("C", 3, 10m), new("D", 4, 10m)];

    [SetUp]
    public void SetUp()
    {
        orders = new(); inventory = new(MockBehavior.Strict); payments = new(); notifications = new();
        discounts = new(); tax = new(); uow = new(); clock = new(); delayer = new(); logger = new();
        orders.Setup(x => x.GetByIdempotencyKeyAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Order?)null);
        orders.Setup(x => x.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        inventory.Setup(x => x.TryReserveAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        inventory.Setup(x => x.ReleaseAsync(It.IsAny<string>(), It.IsAny<int>(), CancellationToken.None)).Returns(Task.CompletedTask);
        payments.Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new PaymentResult(true, false, "tx", null));
        payments.Setup(x => x.RefundAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        discounts.Setup(x => x.GetApplicableAsync(It.IsAny<Customer>(), It.IsAny<IReadOnlyList<OrderLine>>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<Discount>());
        tax.Setup(x => x.Calculate(It.IsAny<string>(), It.IsAny<decimal>())).Returns(0m);
        clock.SetupGet(x => x.UtcNow).Returns(new DateTimeOffset(2026, 10, 14, 12, 0, 0, TimeSpan.Zero));
        uow.Setup(x => x.ExecuteAsync(It.IsAny<Func<Task<bool>>>(), It.IsAny<CancellationToken>())).Returns((Func<Task<bool>> work, CancellationToken _) => work());
        notifications.Setup(x => x.SendConfirmationAsync(It.IsAny<Order>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    }

    private OrderProcessingService Sut() => new(orders.Object, inventory.Object, payments.Object, discounts.Object,
        tax.Object, notifications.Object, uow.Object, clock.Object, delayer.Object, new OrderOptions(), logger.Object);
    private static PlaceOrderRequest Request(OrderLine[]? lines = null) => new(Guid.NewGuid().ToString(),
        new Customer(Guid.NewGuid(), "unit@example.test", CustomerTier.Standard, "RU"), lines ?? Lines(), "token");
    private void NoDownstream()
    {
        payments.Verify(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        orders.Verify(x => x.SaveAsync(It.IsAny<Order>(), It.IsAny<CancellationToken>()), Times.Never);
        notifications.Verify(x => x.SendConfirmationAsync(It.IsAny<Order>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(x => x.ExecuteAsync(It.IsAny<Func<Task<bool>>>(), It.IsAny<CancellationToken>()), Times.Never);
    }
    private void ExactInventory(OrderLine[] lines, int attempted, int released, CancellationToken ct = default)
    {
        for (int i = 0; i < attempted; i++)
            inventory.Verify(x => x.TryReserveAsync(lines[i].Sku, lines[i].Quantity, ct), Times.Once);
        for (int i = 0; i < released; i++)
            inventory.Verify(x => x.ReleaseAsync(lines[i].Sku, lines[i].Quantity, CancellationToken.None), Times.Once);
        inventory.VerifyNoOtherCalls();
    }

    [Test]
    public async Task AllAvailable_ReservesExactSkuAndQuantityBeforeCharging()
    {
        var request = Request();
        payments.Setup(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => { foreach (var line in request.Lines)
                inventory.Verify(x => x.TryReserveAsync(line.Sku, line.Quantity, CancellationToken.None), Times.Once); })
            .ReturnsAsync(new PaymentResult(true, false, "tx", null));
        var result = await Sut().PlaceOrderAsync(request);
        Assert.That(result.Order?.Status, Is.EqualTo(OrderStatus.Confirmed));
        ExactInventory(request.Lines.ToArray(), 4, 0);
        payments.Verify(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public async Task RejectedLine_ReleasesOnlySuccessfulPrefixAndStops(int rejected)
    {
        var lines = Lines();
        inventory.Setup(x => x.TryReserveAsync(lines[rejected].Sku, lines[rejected].Quantity, CancellationToken.None)).ReturnsAsync(false);
        var result = await Sut().PlaceOrderAsync(Request(lines));
        Assert.Multiple(() => {
            Assert.That(result.Failure, Is.EqualTo(FailureReason.OutOfStock));
            Assert.That(result.IsSuccess, Is.False);
            Assert.That(result.Order, Is.Null);
            Assert.That(result.Message, Does.Contain(lines[rejected].Sku));
        });
        ExactInventory(lines, rejected + 1, rejected);
        NoDownstream();
    }

    [Test]
    public async Task PendingReservation_DoesNotStartPayment()
    {
        var gate = Gate<bool>(); var entered = Gate<bool>();
        inventory.Setup(x => x.TryReserveAsync("B", 2, CancellationToken.None)).Returns(() => { entered.TrySetResult(true); return gate.Task; });
        var work = Sut().PlaceOrderAsync(Request());
        try
        {
            await entered.Task.WaitAsync(Timeout);
            Assert.That(work.IsCompleted, Is.False);
            NoDownstream();
            inventory.Verify(x => x.TryReserveAsync("C", 3, CancellationToken.None), Times.Never);
        }
        finally { gate.TrySetResult(true); await work.WaitAsync(Timeout); }
        payments.Verify(x => x.ChargeAsync(It.IsAny<PaymentRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Test]
    public async Task PendingRollback_IsAwaitedBeforeOutOfStockResult()
    {
        var gate = Gate<bool>(); var entered = Gate<bool>();
        inventory.Setup(x => x.TryReserveAsync("B", 2, CancellationToken.None)).ReturnsAsync(false);
        inventory.Setup(x => x.ReleaseAsync("A", 1, CancellationToken.None)).Returns(() => { entered.TrySetResult(true); return gate.Task; });
        var work = Sut().PlaceOrderAsync(Request());
        try { await entered.Task.WaitAsync(Timeout); Assert.That(work.IsCompleted, Is.False); NoDownstream(); }
        finally { gate.TrySetResult(true); await work.WaitAsync(Timeout); }
        Assert.That((await work).Failure, Is.EqualTo(FailureReason.OutOfStock));
        ExactInventory(Lines(), 2, 1);
    }

    [Test]
    public async Task ReleaseThrows_StillAttemptsOtherReservedLines()
    {
        inventory.Setup(x => x.TryReserveAsync("C", 3, CancellationToken.None)).ReturnsAsync(false);
        inventory.Setup(x => x.ReleaseAsync("A", 1, CancellationToken.None)).ThrowsAsync(new InvalidOperationException("release failed"));
        var result = await Sut().PlaceOrderAsync(Request());
        Assert.That(result.Failure, Is.EqualTo(FailureReason.OutOfStock));
        ExactInventory(Lines(), 3, 2); NoDownstream();
    }

    [TestCase(0)]
    [TestCase(1)]
    public void ReserveThrows_ReleasesPrefixAndRethrowsOriginal(int failed)
    {
        var lines = Lines(); var error = new InvalidOperationException("reservation failed");
        inventory.Setup(x => x.TryReserveAsync(lines[failed].Sku, lines[failed].Quantity, CancellationToken.None)).ThrowsAsync(error);
        var actual = Assert.ThrowsAsync<InvalidOperationException>(() => Sut().PlaceOrderAsync(Request(lines)));
        Assert.That(actual, Is.SameAs(error));
        NoDownstream(); ExactInventory(lines, failed + 1, failed);
    }

    [Test]
    public async Task CancellationDuringReservation_ReleasesPrefixWithUncancelledToken()
    {
        using var cts = new CancellationTokenSource(); var entered = Gate<bool>(); var gate = Gate<bool>();
        inventory.Setup(x => x.TryReserveAsync("B", 2, cts.Token)).Returns(() => { entered.TrySetResult(true); return gate.Task.WaitAsync(cts.Token); });
        var work = Sut().PlaceOrderAsync(Request(), cts.Token);
        try { await entered.Task.WaitAsync(Timeout); }
        finally { cts.Cancel(); }
        try { await work.WaitAsync(Timeout); Assert.Fail("Expected cancellation"); }
        catch (OperationCanceledException ex) { Assert.That(ex.CancellationToken, Is.EqualTo(cts.Token)); }
        NoDownstream(); ExactInventory(Lines(), 2, 1, cts.Token);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DuplicateSku_PreservesQuantitiesAndRollbackMultiplicity(bool rejectLast)
    {
        OrderLine[] lines = [new("A", 1, 10m), new("A", 2, 10m), new("B", 3, 10m)];
        inventory.Setup(x => x.TryReserveAsync("B", 3, CancellationToken.None)).ReturnsAsync(!rejectLast);
        var result = await Sut().PlaceOrderAsync(Request(lines));
        Assert.That(result.Failure, Is.EqualTo(rejectLast ? FailureReason.OutOfStock : FailureReason.None));
        ExactInventory(lines, 3, rejectLast ? 2 : 0);
        if (rejectLast) NoDownstream();
    }
}
