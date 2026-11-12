using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Netty.NET.Transport.Channels;
using Netty.NET.Transport.Channels.Groups;

namespace Netty.NET.Transport.Tests;

// Supplemental contracts of ChannelGroup's inherited Set and Comparable APIs, using CLR collection interfaces.
public class ChannelGroupSetContractTest
{
    private static IChannel NewChannel(bool server = false, Task close = null)
    {
        var mock = server ? new Mock<IServerChannel>().As<IChannel>() : new Mock<IChannel>();
        mock.SetupGet(c => c.Id).Returns(DefaultChannelId.NewInstance());
        mock.SetupGet(c => c.CloseCompletion).Returns(close ?? new TaskCompletionSource().Task);
        mock.Setup(c => c.CloseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return mock.Object;
    }
    [Theory]
    [InlineData("union")]
    [InlineData("except")]
    [InlineData("intersect")]
    [InlineData("symmetric")]
    public void BulkMutationRetainsReferenceMembershipAndDeduplicatesItsInput(string operation)
    {
        var a = NewChannel(); var b = NewChannel(); var c = NewChannel(); var d = NewChannel();
        ISet<IChannel> group = new DefaultChannelGroup(); group.Add(a); group.Add(b); group.Add(c);
        var other = new[] { b, d, d };
        if (operation == "union") { group.UnionWith(other); Assert.True(group.SetEquals(new[] { a, b, c, d })); }
        if (operation == "except") { group.ExceptWith(other); Assert.True(group.SetEquals(new[] { a, c })); }
        if (operation == "intersect") { group.IntersectWith(other); Assert.True(group.SetEquals(new[] { b })); }
        if (operation == "symmetric") { group.SymmetricExceptWith(other); Assert.True(group.SetEquals(new[] { a, c, d })); }
        Assert.False(group.IsReadOnly);
        foreach (var channel in new[] { a, b, c, d }) Mock.Get(channel).Verify(c => c.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
    [Theory]
    [InlineData("union", 2)]
    [InlineData("intersect", 2)]
    [InlineData("except", 0)]
    [InlineData("symmetric", 0)]
    public void AGroupCanBeItsOwnBulkOperand(string operation, int count)
    {
        ISet<IChannel> group = new DefaultChannelGroup(); group.Add(NewChannel()); group.Add(NewChannel());
        if (operation == "union") group.UnionWith(group);
        if (operation == "except") group.ExceptWith(group);
        if (operation == "intersect") group.IntersectWith(group);
        if (operation == "symmetric") group.SymmetricExceptWith(group);
        Assert.Equal(count, group.Count);
    }
    [Fact]
    public void SetQueriesIgnoreOperandDuplicatesWithoutChangingGroupIdentity()
    {
        var a = NewChannel(); var b = NewChannel(); var c = NewChannel();
        var group = new DefaultChannelGroup("same"); group.UnionWith(new[] { a, b });
        Assert.True(group.IsSubsetOf(new[] { a, b, b, c }));
        Assert.True(group.IsProperSubsetOf(new[] { a, b, b, c }));
        Assert.True(group.IsSupersetOf(new[] { a, a }));
        Assert.True(group.IsProperSupersetOf(new[] { a, a }));
        Assert.True(group.Overlaps(new[] { c, b })); Assert.False(group.Overlaps(new[] { c }));
        Assert.True(group.SetEquals(new[] { b, a, b })); Assert.False(group.SetEquals(new[] { a }));
        var other = new DefaultChannelGroup("same"); other.UnionWith(group);
        Assert.True(group.SetEquals(other)); Assert.False(group.Equals(other));
        Assert.Equal(RuntimeHelpers.GetHashCode(group), group.GetHashCode());
    }
    [Theory]
    [InlineData("union")]
    [InlineData("except")]
    [InlineData("intersect")]
    [InlineData("symmetric")]
    [InlineData("subset")]
    [InlineData("superset")]
    [InlineData("properSubset")]
    [InlineData("properSuperset")]
    [InlineData("overlaps")]
    [InlineData("equal")]
    public void NullBulkOperandsAreRejected(string operation)
    {
        var group = new DefaultChannelGroup();
        Assert.Throws<ArgumentNullException>(() =>
        {
            switch (operation)
            {
                case "union": group.UnionWith(null); break;
                case "except": group.ExceptWith(null); break;
                case "intersect": group.IntersectWith(null); break;
                case "symmetric": group.SymmetricExceptWith(null); break;
                case "subset": group.IsSubsetOf(null); break;
                case "superset": group.IsSupersetOf(null); break;
                case "properSubset": group.IsProperSubsetOf(null); break;
                case "properSuperset": group.IsProperSupersetOf(null); break;
                case "overlaps": group.Overlaps(null); break;
                default: group.SetEquals(null); break;
            }
        });
    }
    [Fact]
    public void CopyAndArraySnapshotsRetainServerFirstOrderAndDoNotChangeMembership()
    {
        var server = NewChannel(true); var a = NewChannel(); var b = NewChannel();
        var group = new DefaultChannelGroup(); group.UnionWith(new[] { a, server, b });
        var snapshot = group.ToArray(); Assert.Same(server, snapshot[0]);
        var target = new IChannel[5]; ((ICollection<IChannel>)group).CopyTo(target, 1);
        Assert.Null(target[0]); Assert.Null(target[4]); Assert.Same(server, target[1]);
        Assert.True(group.SetEquals(target.Skip(1).Take(3)));
        group.Clear(); Assert.Equal(3, snapshot.Length); Assert.Same(server, snapshot[0]);
    }
    [Fact]
    public void CopyToValidatesTheNativeArrayContract()
    {
        var group = new DefaultChannelGroup(); group.Add(NewChannel());
        Assert.Throws<ArgumentNullException>(() => group.CopyTo(null, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => group.CopyTo(new IChannel[1], -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => group.CopyTo(new IChannel[1], 2));
        Assert.Throws<ArgumentException>(() => group.CopyTo(Array.Empty<IChannel>(), 0));
        Assert.Throws<ArgumentException>(() => group.CopyTo(new IChannel[1], 1));
        group.Clear(); group.CopyTo(Array.Empty<IChannel>(), 0);
    }
    [Theory]
    [InlineData("union")]
    [InlineData("symmetric")]
    public async Task BulkAdmissionStillClosesNewMembersOfAStayClosedGroup(string operation)
    {
        var group = new DefaultChannelGroup(stayClosed: true); await group.CloseAsync();
        var channel = NewChannel();
        if (operation == "union") group.UnionWith(new[] { channel, channel });
        else group.SymmetricExceptWith(new[] { channel, channel });
        Assert.Contains(channel, group);
        // Union performs two original Add calls; symmetric difference deduplicates its set operand.
        Mock.Get(channel).Verify(c => c.CloseAsync(It.IsAny<CancellationToken>()), operation == "union" ? Times.Exactly(2) : Times.Once());
    }
    [Fact(Timeout = 5000)]
    public async Task AutomaticallyClosedMembersAreRemovedAfterCollectionAdmission()
    {
        var close = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var channel = NewChannel(close: close.Task); var group = new DefaultChannelGroup();
        ((ICollection<IChannel>)group).Add(channel); Assert.Contains(channel, group);
        close.SetResult();
        while (group.Contains(channel)) await Task.Delay(1, TestContext.Current.CancellationToken);
        Assert.Empty(group);
    }
    [Fact]
    public void NameOrderingIsOrdinalAndSameNameOrderingRetainsIdentity()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
            var first = new DefaultChannelGroup("I"); var second = new DefaultChannelGroup("i");
            Assert.Equal(Math.Sign(string.CompareOrdinal(first.Name, second.Name)), Math.Sign(first.CompareTo(second)));
            var sameName = new DefaultChannelGroup("I");
            Assert.Equal(RuntimeHelpers.GetHashCode(first).CompareTo(RuntimeHelpers.GetHashCode(sameName)), first.CompareTo(sameName));
            Assert.Equal(0, first.CompareTo(first)); Assert.True(first.CompareTo(null) > 0);
            int hash = first.GetHashCode(); first.Add(NewChannel()); Assert.Equal(hash, first.GetHashCode());
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }
    [Fact(Timeout = 10000)]
    public async Task ConcurrentAdmissionAndRemovalKeepArraySnapshotsConsistent()
    {
        var channels = Enumerable.Range(0, 64).Select(i => NewChannel(i % 8 == 0)).ToArray();
        var group = new DefaultChannelGroup(); var cancellation = TestContext.Current.CancellationToken;
        Task mutation = Task.Run(() =>
        {
            for (int repeat = 0; repeat < 100; repeat++) { group.UnionWith(channels); group.ExceptWith(channels); }
        }, cancellation);
        while (!mutation.IsCompleted)
        {
            var snapshot = group.ToArray(); Assert.DoesNotContain(snapshot, item => item == null);
            Assert.Equal(snapshot.Length, snapshot.Distinct(ReferenceEqualityComparer.Instance).Count());
            Assert.True(snapshot.TakeWhile(c => c is IServerChannel).Count() == snapshot.Count(c => c is IServerChannel));
            await Task.Yield();
        }
        await mutation;
    }
}
