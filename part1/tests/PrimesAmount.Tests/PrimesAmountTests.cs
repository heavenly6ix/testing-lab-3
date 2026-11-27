using System.Collections.Concurrent;
using async_tests;
using Moq;
using NUnit.Framework;

namespace Part1.Primes.Tests;

[TestFixture]
public class PrimesAmountTests
{
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(10)]
    public void Constructor_PositiveBorder_PreservesParameter(int border) =>
        Assert.That(new PrimesAmount(border).Border, Is.EqualTo(border));

    [TestCase(0)]
    [TestCase(-5)]
    public void Constructor_NonPositiveBorder_Throws(int border) =>
        Assert.Throws<ArgumentException>(() => new PrimesAmount(border));

    [Test]
    public void CountPrimesAmount_Ten_ReturnsPrimesWithoutOrder() =>
        Assert.That(new PrimesAmount(10).CountPrimesAmount(new ConcretePrimeService()),
            Is.EquivalentTo(new[] { 2, 3, 5, 7 }));

    [TestCase(1)]
    [TestCase(2)]
    public void CountPrimesAmount_SmallBorder_ReturnsEmpty(int border) =>
        Assert.That(new PrimesAmount(border).CountPrimesAmount(new ConcretePrimeService()), Is.Empty);

    [Test]
    public void CountPrimesAmount_SubstituteService_UsesItsAnswersOncePerCandidate()
    {
        var calls = new ConcurrentDictionary<int, int>();
        var service = new Mock<PrimeService>(MockBehavior.Strict);
        service.Setup(s => s.CheckIsPrime(It.IsAny<int>())).Returns((int n) =>
        {
            calls.AddOrUpdate(n, 1, (_, old) => old + 1);
            return n is 4 or 8; // Deliberately composite: verifies substitution.
        });
        service.Setup(s => s.ClearCache());
        var result = new PrimesAmount(10).CountPrimesAmount(service.Object);
        Assert.That(result, Is.EquivalentTo(new[] { 4, 8 }));
        Assert.That(calls.Keys, Is.EquivalentTo(Enumerable.Range(2, 8)));
        Assert.That(calls.Values, Is.All.EqualTo(1));
        foreach (int n in Enumerable.Range(2, 8))
            service.Verify(s => s.CheckIsPrime(n), Times.Once);
        service.Verify(s => s.ClearCache(), Times.Once);
        service.VerifyNoOtherCalls();
    }

    [TestCase(1, 0)]
    [TestCase(2, 1)]
    public void CountPrimesAmount_EmptyRange_FollowsCacheContract(int border, int clearCalls)
    {
        var service = new Mock<PrimeService>(MockBehavior.Strict);
        service.Setup(s => s.ClearCache());
        Assert.That(new PrimesAmount(border).CountPrimesAmount(service.Object), Is.Empty);
        service.Verify(s => s.CheckIsPrime(It.IsAny<int>()), Times.Never);
        service.Verify(s => s.ClearCache(), Times.Exactly(clearCalls));
    }

    [Test]
    public void CountPrimesAmount_CheckThrows_ClearsPartialResultAndCache()
    {
        var service = new Mock<PrimeService>();
        service.Setup(s => s.CheckIsPrime(It.IsAny<int>())).Returns(true);
        service.Setup(s => s.CheckIsPrime(5)).Throws(new InvalidOperationException("check failed"));
        var sut = new PrimesAmount(10);
        Assert.That(sut.CountPrimesAmount(service.Object), Is.Empty);
        Assert.That(sut.Count, Is.Zero);
        service.Verify(s => s.CheckIsPrime(5), Times.Once);
        service.Verify(s => s.ClearCache(), Times.Once);
        // Parallel.For may stop scheduling after an exception; no exact call count for other candidates.
    }

    [Test]
    public void CountPrimesAmount_RepeatedCall_ReplacesPreviousCountAndResult()
    {
        var service = new Mock<PrimeService>();
        service.Setup(s => s.CheckIsPrime(It.IsAny<int>())).Returns(true);
        var sut = new PrimesAmount(10);
        var first = sut.CountPrimesAmount(service.Object);
        Assert.That(first.Count, Is.EqualTo(8));
        Assert.That(sut.Count, Is.EqualTo(8));
        service.Setup(s => s.CheckIsPrime(It.IsAny<int>())).Returns(false);
        var second = sut.CountPrimesAmount(service.Object);
        Assert.That(second, Is.Empty);
        Assert.That(sut.Count, Is.Zero);
        Assert.That(first.Count, Is.EqualTo(8), "previous returned queue is independent");
        service.Verify(s => s.ClearCache(), Times.Exactly(2));
    }

    [TestCase(10, 4)]
    [TestCase(1, 0)]
    [TestCase(2, 0)]
    public void Count_AfterCalculation_IncludesValidZero(int border, int expected)
    {
        var sut = new PrimesAmount(border);
        sut.CountPrimesAmount(new ConcretePrimeService());
        Assert.That(sut.Count, Is.EqualTo(expected));
    }

    [TestCase(1, false)]
    [TestCase(2, true)]
    [TestCase(3, true)]
    [TestCase(9, false)]
    [TestCase(25, false)]
    public void ConcreteService_ClassifiesNumbers(int value, bool expected) =>
        Assert.That(new ConcretePrimeService().CheckIsPrime(value), Is.EqualTo(expected));
}
