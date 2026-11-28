/*
 * Copyright 2017 The Netty Project
 *
 * The Netty Project licenses this file to you under the Apache License, version 2.0 (the
 * "License"); you may not use this file except in compliance with the License. You may obtain a
 * copy of the License at:
 *
 * https://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software distributed under the License
 * is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express
 * or implied. See the License for the specific language governing permissions and limitations under
 * the License.
 */

using Moq;
using static Netty.NET.Handler.Codec.Http2.Http2CodecUtil;
using static Netty.NET.Handler.Codec.Http2.WeightedFairQueueByteDistributor;

namespace Netty.NET.Handler.Codec.Http2.Tests;

// CLR: @BeforeEach setup() is the constructor (xUnit creates a test class instance per test,
// https://xunit.net/docs/shared-context) and MockitoAnnotations.initMocks is the Moq writer field of the base class;
// doAnswer(writeAnswer(false)).when(writer).write(any, anyInt) is a Write setup with the base class callback (Moq
// Quickstart, https://github.com/devlooped/moq/wiki/Quickstart). The anonymous Http2StreamVisitor of
// closeWhileIteratingDoesNotNPE is the private nested CloseWhileIteratingVisitor. The package-private isChild/
// numChildren and INITIAL_CHILDREN_MAP_SIZE are internal and reached through InternalsVisibleTo.
public class WeightedFairQueueByteDistributorDependencyTreeTest :
                                                AbstractWeightedFairQueueByteDistributorDependencyTest
{
    private const int leadersId = 3; // js, css
    private const int unblockedId = 5;
    private const int backgroundId = 7;
    private const int speculativeId = 9;
    private const int followersId = 11; // images
    private const short leadersWeight = 201;
    private const short unblockedWeight = 101;
    private const short backgroundWeight = 1;
    private const short speculativeWeight = 1;
    private const short followersWeight = 1;

    public WeightedFairQueueByteDistributorDependencyTreeTest()
    {
        Setup(0);
    }

    private void Setup(int maxStateOnlySize)
    {
        connection = new DefaultHttp2Connection(false);
        distributor = new WeightedFairQueueByteDistributor(connection, maxStateOnlySize);

        // Assume we always write all the allocated bytes.
        writer.Setup(w => w.Write(It.IsAny<IHttp2Stream>(), It.IsAny<int>())).Callback(WriteAnswer(false));
    }

    [Fact]
    public void ClosingStreamWithChildrenDoesNotCauseConcurrentModification()
    {
        // We create enough streams to wrap around the child array. We carefully craft the stream ids so that they hash
        // codes overlap with respect to the child collection. If the implementation is not careful this may lead to a
        // concurrent modification exception while promoting all children to the connection stream.
        IHttp2Stream streamA = connection.Local().CreateStream(1, false);
        int numStreams = INITIAL_CHILDREN_MAP_SIZE - 1;

        for (int i = 0, streamId = 3; i < numStreams; ++i, streamId += INITIAL_CHILDREN_MAP_SIZE)
        {
            IHttp2Stream stream = connection.Local().CreateStream(streamId, false);
            SetPriority(stream.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        }

        Assert.Equal(INITIAL_CHILDREN_MAP_SIZE, connection.NumActiveStreams());
        streamA.Close();
        Assert.Equal(numStreams, connection.NumActiveStreams());
    }

    [Fact]
    public void CloseWhileIteratingDoesNotNPE()
    {
        IHttp2Stream streamA = connection.Local().CreateStream(3, false);
        IHttp2Stream streamB = connection.Local().CreateStream(5, false);
        IHttp2Stream streamC = connection.Local().CreateStream(7, false);
        SetPriority(streamB.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        connection.ForEachActiveStream(new CloseWhileIteratingVisitor(this, streamA, streamB, streamC));
    }

    // CLR: the anonymous Http2StreamVisitor of closeWhileIteratingDoesNotNPE.
    private sealed class CloseWhileIteratingVisitor : IHttp2StreamVisitor
    {
        private readonly WeightedFairQueueByteDistributorDependencyTreeTest test;
        private readonly IHttp2Stream streamA;
        private readonly IHttp2Stream streamB;
        private readonly IHttp2Stream streamC;

        internal CloseWhileIteratingVisitor(WeightedFairQueueByteDistributorDependencyTreeTest test,
                                            IHttp2Stream streamA, IHttp2Stream streamB, IHttp2Stream streamC)
        {
            this.test = test;
            this.streamA = streamA;
            this.streamB = streamB;
            this.streamC = streamC;
        }

        public bool Visit(IHttp2Stream stream)
        {
            streamA.Close();
            test.SetPriority(streamB.Id(), streamC.Id(), DEFAULT_PRIORITY_WEIGHT, false);
            return true;
        }
    }

    [Fact]
    public void LocalStreamCanDependUponIdleStream()
    {
        Setup(1);

        IHttp2Stream streamA = connection.Local().CreateStream(1, false);
        SetPriority(3, streamA.Id(), MIN_WEIGHT, true);
        Assert.True(distributor.IsChild(3, streamA.Id(), MIN_WEIGHT));
    }

    [Fact]
    public void RemoteStreamCanDependUponIdleStream()
    {
        Setup(1);

        IHttp2Stream streamA = connection.Remote().CreateStream(2, false);
        SetPriority(4, streamA.Id(), MIN_WEIGHT, true);
        Assert.True(distributor.IsChild(4, streamA.Id(), MIN_WEIGHT));
    }

    [Fact]
    public void PrioritizeShouldUseDefaults()
    {
        IHttp2Stream stream = connection.Local().CreateStream(1, false);
        Assert.True(distributor.IsChild(stream.Id(), connection.ConnectionStream().Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(1, distributor.NumChildren(connection.ConnectionStream().Id()));
        Assert.Equal(0, distributor.NumChildren(stream.Id()));
    }

    [Fact]
    public void ReprioritizeWithNoChangeShouldDoNothing()
    {
        IHttp2Stream stream = connection.Local().CreateStream(1, false);
        SetPriority(stream.Id(), connection.ConnectionStream().Id(), DEFAULT_PRIORITY_WEIGHT, false);
        Assert.True(distributor.IsChild(stream.Id(), connection.ConnectionStream().Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(1, distributor.NumChildren(connection.ConnectionStream().Id()));
        Assert.Equal(0, distributor.NumChildren(stream.Id()));
    }

    [Fact]
    public void StateOnlyPriorityShouldBePreservedWhenStreamsAreCreatedAndClosed()
    {
        Setup(3);

        short weight3 = MIN_WEIGHT + 1;
        short weight5 = (short) (weight3 + 1);
        short weight7 = (short) (weight5 + 1);
        SetPriority(3, connection.ConnectionStream().Id(), weight3, true);
        SetPriority(5, connection.ConnectionStream().Id(), weight5, true);
        SetPriority(7, connection.ConnectionStream().Id(), weight7, true);

        Assert.Equal(0, connection.NumActiveStreams());
        VerifyStateOnlyPriorityShouldBePreservedWhenStreamsAreCreated(weight3, weight5, weight7);

        // Now create stream objects and ensure the state and dependency tree is preserved.
        IHttp2Stream streamA = connection.Local().CreateStream(3, false);
        IHttp2Stream streamB = connection.Local().CreateStream(5, false);
        IHttp2Stream streamC = connection.Local().CreateStream(7, false);

        Assert.Equal(3, connection.NumActiveStreams());
        VerifyStateOnlyPriorityShouldBePreservedWhenStreamsAreCreated(weight3, weight5, weight7);

        // Close all the streams and ensure the state and dependency tree is preserved.
        streamA.Close();
        streamB.Close();
        streamC.Close();

        Assert.Equal(0, connection.NumActiveStreams());
        VerifyStateOnlyPriorityShouldBePreservedWhenStreamsAreCreated(weight3, weight5, weight7);
    }

    private void VerifyStateOnlyPriorityShouldBePreservedWhenStreamsAreCreated(short weight3, short weight5,
                                                                               short weight7)
                                                                               {
        // Level 0
        Assert.Equal(1, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        Assert.True(distributor.IsChild(7, connection.ConnectionStream().Id(), weight7));
        Assert.Equal(1, distributor.NumChildren(7));

        // Level 2
        Assert.True(distributor.IsChild(5, 7, weight5));
        Assert.Equal(1, distributor.NumChildren(5));

        // Level 3
        Assert.True(distributor.IsChild(3, 5, weight3));
        Assert.Equal(0, distributor.NumChildren(3));
    }

    [Fact]
    public void FireFoxQoSStreamsRemainAfterDataStreamsAreClosed()
    {
        // https://bitsup.blogspot.com/2015/01/http2-dependency-priorities-in-firefox.html
        Setup(5);

        SetPriority(leadersId, connection.ConnectionStream().Id(), leadersWeight, false);
        SetPriority(unblockedId, connection.ConnectionStream().Id(), unblockedWeight, false);
        SetPriority(backgroundId, connection.ConnectionStream().Id(), backgroundWeight, false);
        SetPriority(speculativeId, backgroundId, speculativeWeight, false);
        SetPriority(followersId, leadersId, followersWeight, false);

        VerifyFireFoxQoSStreams();

        // Simulate a HTML request
        short htmlGetStreamWeight = 2;
        IHttp2Stream htmlGetStream = connection.Local().CreateStream(13, false);
        SetPriority(htmlGetStream.Id(), followersId, htmlGetStreamWeight, false);
        IHttp2Stream favIconStream = connection.Local().CreateStream(15, false);
        SetPriority(favIconStream.Id(), connection.ConnectionStream().Id(), DEFAULT_PRIORITY_WEIGHT, false);
        IHttp2Stream cssStream = connection.Local().CreateStream(17, false);
        SetPriority(cssStream.Id(), leadersId, DEFAULT_PRIORITY_WEIGHT, false);
        IHttp2Stream jsStream = connection.Local().CreateStream(19, false);
        SetPriority(jsStream.Id(), leadersId, DEFAULT_PRIORITY_WEIGHT, false);
        IHttp2Stream imageStream = connection.Local().CreateStream(21, false);
        SetPriority(imageStream.Id(), followersId, 1, false);

        // Level 0
        Assert.Equal(4, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        Assert.True(distributor.IsChild(leadersId, connection.ConnectionStream().Id(), leadersWeight));
        Assert.Equal(3, distributor.NumChildren(leadersId));

        Assert.True(distributor.IsChild(unblockedId, connection.ConnectionStream().Id(), unblockedWeight));
        Assert.Equal(0, distributor.NumChildren(unblockedId));

        Assert.True(distributor.IsChild(backgroundId, connection.ConnectionStream().Id(), backgroundWeight));
        Assert.Equal(1, distributor.NumChildren(backgroundId));

        Assert.True(distributor.IsChild(favIconStream.Id(), connection.ConnectionStream().Id(),
                                       DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(favIconStream.Id()));

        // Level 2
        Assert.True(distributor.IsChild(followersId, leadersId, followersWeight));
        Assert.Equal(2, distributor.NumChildren(followersId));

        Assert.True(distributor.IsChild(speculativeId, backgroundId, speculativeWeight));
        Assert.Equal(0, distributor.NumChildren(speculativeId));

        Assert.True(distributor.IsChild(cssStream.Id(), leadersId, DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(cssStream.Id()));

        Assert.True(distributor.IsChild(jsStream.Id(), leadersId, DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(jsStream.Id()));

        // Level 3
        Assert.True(distributor.IsChild(htmlGetStream.Id(), followersId, htmlGetStreamWeight));
        Assert.Equal(0, distributor.NumChildren(htmlGetStream.Id()));

        Assert.True(distributor.IsChild(imageStream.Id(), followersId, followersWeight));
        Assert.Equal(0, distributor.NumChildren(imageStream.Id()));

        // Close all the data streams and ensure the "priority only streams" are retained in the dependency tree.
        htmlGetStream.Close();
        favIconStream.Close();
        cssStream.Close();
        jsStream.Close();
        imageStream.Close();

        VerifyFireFoxQoSStreams();
    }

    private void VerifyFireFoxQoSStreams()
    {
        // Level 0
        Assert.Equal(3, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        Assert.True(distributor.IsChild(leadersId, connection.ConnectionStream().Id(), leadersWeight));
        Assert.Equal(1, distributor.NumChildren(leadersId));

        Assert.True(distributor.IsChild(unblockedId, connection.ConnectionStream().Id(), unblockedWeight));
        Assert.Equal(0, distributor.NumChildren(unblockedId));

        Assert.True(distributor.IsChild(backgroundId, connection.ConnectionStream().Id(), backgroundWeight));
        Assert.Equal(1, distributor.NumChildren(backgroundId));

        // Level 2
        Assert.True(distributor.IsChild(followersId, leadersId, followersWeight));
        Assert.Equal(0, distributor.NumChildren(followersId));

        Assert.True(distributor.IsChild(speculativeId, backgroundId, speculativeWeight));
        Assert.Equal(0, distributor.NumChildren(speculativeId));
    }

    [Fact]
    public void LowestPrecedenceStateShouldBeDropped()
    {
        Setup(3);

        short weight3 = MAX_WEIGHT;
        short weight5 = (short) (weight3 - 1);
        short weight7 = (short) (weight5 - 1);
        short weight9 = (short) (weight7 - 1);
        SetPriority(3, connection.ConnectionStream().Id(), weight3, true);
        SetPriority(5, connection.ConnectionStream().Id(), weight5, true);
        SetPriority(7, connection.ConnectionStream().Id(), weight7, false);
        Assert.Equal(0, connection.NumActiveStreams());
        VerifyLowestPrecedenceStateShouldBeDropped1(weight3, weight5, weight7);

        // Attempt to create a new item in the dependency tree but the maximum amount of "state only" streams is meet
        // so a stream will have to be dropped. Currently the new stream is the lowest "precedence" so it is dropped.
        SetPriority(9, 3, weight9, false);
        Assert.Equal(0, connection.NumActiveStreams());
        VerifyLowestPrecedenceStateShouldBeDropped1(weight3, weight5, weight7);

        // Set the priority for stream 9 such that its depth in the dependency tree is numerically lower than stream 3,
        // and therefore the dependency state associated with stream 3 will be dropped.
        SetPriority(9, 5, weight9, true);
        VerifyLowestPrecedenceStateShouldBeDropped2(weight9, weight5, weight7);

        // Test that stream which has been activated is lower priority than other streams that have not been activated.
        IHttp2Stream streamA = connection.Local().CreateStream(5, false);
        streamA.Close();
        VerifyLowestPrecedenceStateShouldBeDropped2(weight9, weight5, weight7);

        // Stream 3 (hasn't been opened) should result in stream 5 being dropped.
        // dropping stream 5 will distribute its weight to children (only 9)
        SetPriority(3, 9, weight3, false);
        VerifyLowestPrecedenceStateShouldBeDropped3(weight3, weight7, weight5);

        // Stream 5's state has been discarded so we should be able to re-insert this state.
        SetPriority(5, 0, weight5, false);
        VerifyLowestPrecedenceStateShouldBeDropped4(weight5, weight7, weight5);

        // All streams are at the same level, so stream ID should be used to drop the numeric lowest valued stream.
        short weight11 = (short) (weight9 - 1);
        SetPriority(11, 0, weight11, false);
        VerifyLowestPrecedenceStateShouldBeDropped5(weight7, weight5, weight11);
    }

    private void VerifyLowestPrecedenceStateShouldBeDropped1(short weight3, short weight5, short weight7)
    {
        // Level 0
        Assert.Equal(2, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        Assert.True(distributor.IsChild(7, connection.ConnectionStream().Id(), weight7));
        Assert.Equal(0, distributor.NumChildren(7));

        Assert.True(distributor.IsChild(5, connection.ConnectionStream().Id(), weight5));
        Assert.Equal(1, distributor.NumChildren(5));

        // Level 2
        Assert.True(distributor.IsChild(3, 5, weight3));
        Assert.Equal(0, distributor.NumChildren(3));
    }

    private void VerifyLowestPrecedenceStateShouldBeDropped2(short weight9, short weight5, short weight7)
    {
        // Level 0
        Assert.Equal(2, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        Assert.True(distributor.IsChild(7, connection.ConnectionStream().Id(), weight7));
        Assert.Equal(0, distributor.NumChildren(7));

        Assert.True(distributor.IsChild(5, connection.ConnectionStream().Id(), weight5));
        Assert.Equal(1, distributor.NumChildren(5));

        // Level 2
        Assert.True(distributor.IsChild(9, 5, weight9));
        Assert.Equal(0, distributor.NumChildren(9));
    }

    private void VerifyLowestPrecedenceStateShouldBeDropped3(short weight3, short weight7, short weight9)
    {
        // Level 0
        Assert.Equal(2, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        Assert.True(distributor.IsChild(7, connection.ConnectionStream().Id(), weight7));
        Assert.Equal(0, distributor.NumChildren(7));

        Assert.True(distributor.IsChild(9, connection.ConnectionStream().Id(), weight9));
        Assert.Equal(1, distributor.NumChildren(9));

        // Level 2
        Assert.True(distributor.IsChild(3, 9, weight3));
        Assert.Equal(0, distributor.NumChildren(3));
    }

    private void VerifyLowestPrecedenceStateShouldBeDropped4(short weight5, short weight7, short weight9)
    {
        // Level 0
        Assert.Equal(3, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        Assert.True(distributor.IsChild(5, connection.ConnectionStream().Id(), weight5));
        Assert.Equal(0, distributor.NumChildren(5));

        Assert.True(distributor.IsChild(7, connection.ConnectionStream().Id(), weight7));
        Assert.Equal(0, distributor.NumChildren(7));

        Assert.True(distributor.IsChild(9, connection.ConnectionStream().Id(), weight9));
        Assert.Equal(0, distributor.NumChildren(9));
    }

    private void VerifyLowestPrecedenceStateShouldBeDropped5(short weight7, short weight9, short weight11)
    {
        // Level 0
        Assert.Equal(3, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        Assert.True(distributor.IsChild(11, connection.ConnectionStream().Id(), weight11));
        Assert.Equal(0, distributor.NumChildren(11));

        Assert.True(distributor.IsChild(7, connection.ConnectionStream().Id(), weight7));
        Assert.Equal(0, distributor.NumChildren(7));

        Assert.True(distributor.IsChild(9, connection.ConnectionStream().Id(), weight9));
        Assert.Equal(0, distributor.NumChildren(9));
    }

    [Fact]
    public void PriorityOnlyStreamsArePreservedWhenReservedStreamsAreClosed()
    {
        Setup(1);

        short weight3 = MIN_WEIGHT;
        SetPriority(3, connection.ConnectionStream().Id(), weight3, true);

        IHttp2Stream streamA = connection.Local().CreateStream(5, false);
        IHttp2Stream streamB = connection.Remote().ReservePushStream(4, streamA);

        // Level 0
        Assert.Equal(3, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        Assert.True(distributor.IsChild(3, connection.ConnectionStream().Id(), weight3));
        Assert.Equal(0, distributor.NumChildren(3));

        Assert.True(distributor.IsChild(streamA.Id(), connection.ConnectionStream().Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamA.Id()));

        Assert.True(distributor.IsChild(streamB.Id(), connection.ConnectionStream().Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamB.Id()));

        // Close both streams.
        streamB.Close();
        streamA.Close();

        // Level 0
        Assert.Equal(1, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        Assert.True(distributor.IsChild(3, connection.ConnectionStream().Id(), weight3));
        Assert.Equal(0, distributor.NumChildren(3));
    }

    [Fact]
    public void InsertExclusiveShouldAddNewLevel()
    {
        IHttp2Stream streamA = connection.Local().CreateStream(1, false);
        IHttp2Stream streamB = connection.Local().CreateStream(3, false);
        IHttp2Stream streamC = connection.Local().CreateStream(5, false);
        IHttp2Stream streamD = connection.Local().CreateStream(7, false);

        SetPriority(streamB.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamC.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamD.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, true);

        Assert.Equal(4, connection.NumActiveStreams());

        // Level 0
        Assert.Equal(1, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        Assert.True(distributor.IsChild(streamA.Id(), connection.ConnectionStream().Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(1, distributor.NumChildren(streamA.Id()));

        // Level 2
        Assert.True(distributor.IsChild(streamD.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(2, distributor.NumChildren(streamD.Id()));

        // Level 3
        Assert.True(distributor.IsChild(streamB.Id(), streamD.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamB.Id()));

        Assert.True(distributor.IsChild(streamC.Id(), streamD.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamC.Id()));
    }

    [Fact]
    public void ExistingChildMadeExclusiveShouldNotCreateTreeCycle()
    {
        IHttp2Stream streamA = connection.Local().CreateStream(1, false);
        IHttp2Stream streamB = connection.Local().CreateStream(3, false);
        IHttp2Stream streamC = connection.Local().CreateStream(5, false);
        IHttp2Stream streamD = connection.Local().CreateStream(7, false);

        SetPriority(streamB.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamC.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamD.Id(), streamC.Id(), DEFAULT_PRIORITY_WEIGHT, false);

        // Stream C is already dependent on Stream A, but now make that an exclusive dependency
        SetPriority(streamC.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, true);

        Assert.Equal(4, connection.NumActiveStreams());

        // Level 0
        Assert.Equal(1, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        Assert.True(distributor.IsChild(streamA.Id(), connection.ConnectionStream().Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(1, distributor.NumChildren(streamA.Id()));

        // Level 2
        Assert.True(distributor.IsChild(streamC.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(2, distributor.NumChildren(streamC.Id()));

        // Level 3
        Assert.True(distributor.IsChild(streamB.Id(), streamC.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamB.Id()));

        Assert.True(distributor.IsChild(streamD.Id(), streamC.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamD.Id()));
    }

    [Fact]
    public void NewExclusiveChildShouldUpdateOldParentCorrectly()
    {
        IHttp2Stream streamA = connection.Local().CreateStream(1, false);
        IHttp2Stream streamB = connection.Local().CreateStream(3, false);
        IHttp2Stream streamC = connection.Local().CreateStream(5, false);
        IHttp2Stream streamD = connection.Local().CreateStream(7, false);
        IHttp2Stream streamE = connection.Local().CreateStream(9, false);
        IHttp2Stream streamF = connection.Local().CreateStream(11, false);

        SetPriority(streamB.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamC.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamD.Id(), streamC.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamF.Id(), streamE.Id(), DEFAULT_PRIORITY_WEIGHT, false);

        // F is now going to be exclusively dependent on A, after this we should check that stream E
        // prioritizableForTree is not over decremented.
        SetPriority(streamF.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, true);

        Assert.Equal(6, connection.NumActiveStreams());

        // Level 0
        Assert.Equal(2, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        Assert.True(distributor.IsChild(streamE.Id(), connection.ConnectionStream().Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamE.Id()));

        Assert.True(distributor.IsChild(streamA.Id(), connection.ConnectionStream().Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(1, distributor.NumChildren(streamA.Id()));

        // Level 2
        Assert.True(distributor.IsChild(streamF.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(2, distributor.NumChildren(streamF.Id()));

        // Level 3
        Assert.True(distributor.IsChild(streamB.Id(), streamF.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamB.Id()));

        Assert.True(distributor.IsChild(streamC.Id(), streamF.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(1, distributor.NumChildren(streamC.Id()));

        // Level 4
        Assert.True(distributor.IsChild(streamD.Id(), streamC.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamD.Id()));
    }

    [Fact]
    public void WeightChangeWithNoTreeChangeShouldBeRespected()
    {
        IHttp2Stream streamA = connection.Local().CreateStream(1, false);
        IHttp2Stream streamB = connection.Local().CreateStream(3, false);
        IHttp2Stream streamC = connection.Local().CreateStream(5, false);
        IHttp2Stream streamD = connection.Local().CreateStream(7, false);

        SetPriority(streamB.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamC.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamD.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, true);

        Assert.Equal(4, connection.NumActiveStreams());

        short newWeight = (short) (DEFAULT_PRIORITY_WEIGHT + 1);
        SetPriority(streamD.Id(), streamA.Id(), newWeight, false);

        // Level 0
        Assert.Equal(1, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        Assert.True(distributor.IsChild(streamA.Id(), connection.ConnectionStream().Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(1, distributor.NumChildren(streamA.Id()));

        // Level 2
        Assert.True(distributor.IsChild(streamD.Id(), streamA.Id(), newWeight));
        Assert.Equal(2, distributor.NumChildren(streamD.Id()));

        // Level 3
        Assert.True(distributor.IsChild(streamB.Id(), streamD.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamB.Id()));

        Assert.True(distributor.IsChild(streamC.Id(), streamD.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamC.Id()));
    }

    [Fact]
    public void SameNodeDependentShouldNotStackOverflowNorChangePrioritizableForTree()
    {
        IHttp2Stream streamA = connection.Local().CreateStream(1, false);
        IHttp2Stream streamB = connection.Local().CreateStream(3, false);
        IHttp2Stream streamC = connection.Local().CreateStream(5, false);
        IHttp2Stream streamD = connection.Local().CreateStream(7, false);

        SetPriority(streamB.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamC.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamD.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, true);

        bool[] exclusives = { true, false };
        short[] weights = { DEFAULT_PRIORITY_WEIGHT, 100, 200, DEFAULT_PRIORITY_WEIGHT };

        Assert.Equal(4, connection.NumActiveStreams());

        // The goal is to call setPriority with the same parent and vary the parameters
        // we were at one point adding a circular depends to the tree and then throwing
        // a StackOverflow due to infinite recursive operation.
        foreach (short weight in weights)
        {
            foreach (bool exclusive in exclusives)
            {
                SetPriority(streamD.Id(), streamA.Id(), weight, exclusive);

                Assert.Equal(0, distributor.NumChildren(streamB.Id()));
                Assert.Equal(0, distributor.NumChildren(streamC.Id()));
                Assert.Equal(1, distributor.NumChildren(streamA.Id()));
                Assert.Equal(2, distributor.NumChildren(streamD.Id()));
                Assert.False(distributor.IsChild(streamB.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT));
                Assert.False(distributor.IsChild(streamC.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT));
                Assert.True(distributor.IsChild(streamB.Id(), streamD.Id(), DEFAULT_PRIORITY_WEIGHT));
                Assert.True(distributor.IsChild(streamC.Id(), streamD.Id(), DEFAULT_PRIORITY_WEIGHT));
                Assert.True(distributor.IsChild(streamD.Id(), streamA.Id(), weight));
            }
        }
    }

    [Fact]
    public void MultipleCircularDependencyShouldUpdatePrioritizable()
    {
        IHttp2Stream streamA = connection.Local().CreateStream(1, false);
        IHttp2Stream streamB = connection.Local().CreateStream(3, false);
        IHttp2Stream streamC = connection.Local().CreateStream(5, false);
        IHttp2Stream streamD = connection.Local().CreateStream(7, false);

        SetPriority(streamB.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamC.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamD.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, true);

        Assert.Equal(4, connection.NumActiveStreams());

        // Bring B to the root
        SetPriority(streamA.Id(), streamB.Id(), DEFAULT_PRIORITY_WEIGHT, true);

        // Move all streams to be children of B
        SetPriority(streamC.Id(), streamB.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamD.Id(), streamB.Id(), DEFAULT_PRIORITY_WEIGHT, false);

        // Move A back to the root
        SetPriority(streamB.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, true);

        // Move all streams to be children of A
        SetPriority(streamC.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamD.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);

        // Level 0
        Assert.Equal(1, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        Assert.True(distributor.IsChild(streamA.Id(), connection.ConnectionStream().Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(3, distributor.NumChildren(streamA.Id()));

        // Level 2
        Assert.True(distributor.IsChild(streamB.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamB.Id()));

        Assert.True(distributor.IsChild(streamC.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamC.Id()));

        Assert.True(distributor.IsChild(streamD.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamD.Id()));
    }

    [Fact]
    public void RemoveWithPrioritizableDependentsShouldNotRestructureTree()
    {
        IHttp2Stream streamA = connection.Local().CreateStream(1, false);
        IHttp2Stream streamB = connection.Local().CreateStream(3, false);
        IHttp2Stream streamC = connection.Local().CreateStream(5, false);
        IHttp2Stream streamD = connection.Local().CreateStream(7, false);

        SetPriority(streamB.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamC.Id(), streamB.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamD.Id(), streamB.Id(), DEFAULT_PRIORITY_WEIGHT, false);

        // Default removal policy will cause it to be removed immediately.
        // Closing streamB will distribute its weight to the children (C & D) equally.
        streamB.Close();

        // Level 0
        Assert.Equal(1, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        Assert.True(distributor.IsChild(streamA.Id(), connection.ConnectionStream().Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(2, distributor.NumChildren(streamA.Id()));

        // Level 2
        short halfWeight = DEFAULT_PRIORITY_WEIGHT / 2;
        Assert.True(distributor.IsChild(streamC.Id(), streamA.Id(), halfWeight));
        Assert.Equal(0, distributor.NumChildren(streamC.Id()));

        Assert.True(distributor.IsChild(streamD.Id(), streamA.Id(), halfWeight));
        Assert.Equal(0, distributor.NumChildren(streamD.Id()));
    }

    [Fact]
    public void CloseWithNoPrioritizableDependentsShouldRestructureTree()
    {
        IHttp2Stream streamA = connection.Local().CreateStream(1, false);
        IHttp2Stream streamB = connection.Local().CreateStream(3, false);
        IHttp2Stream streamC = connection.Local().CreateStream(5, false);
        IHttp2Stream streamD = connection.Local().CreateStream(7, false);
        IHttp2Stream streamE = connection.Local().CreateStream(9, false);
        IHttp2Stream streamF = connection.Local().CreateStream(11, false);

        SetPriority(streamB.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamC.Id(), streamB.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamD.Id(), streamB.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamE.Id(), streamC.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamF.Id(), streamD.Id(), DEFAULT_PRIORITY_WEIGHT, false);

        // Close internal nodes, leave 1 leaf node open, the only remaining stream is the one that is not closed (E).
        streamA.Close();
        // Closing streamB will distribute its weight to the children (C & D) equally.
        streamB.Close();
        streamC.Close();
        streamD.Close();
        streamF.Close();

        // Level 0
        Assert.Equal(1, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        short halfWeight = DEFAULT_PRIORITY_WEIGHT / 2;
        Assert.True(distributor.IsChild(streamE.Id(), connection.ConnectionStream().Id(), halfWeight));
        Assert.Equal(0, distributor.NumChildren(streamE.Id()));
    }

    [Fact]
    public void CloseStreamWithChildrenShouldRedistributeWeightToChildren()
    {
        IHttp2Stream streamA = connection.Local().CreateStream(1, false);
        IHttp2Stream streamB = connection.Local().CreateStream(3, false);
        IHttp2Stream streamC = connection.Local().CreateStream(5, false);
        IHttp2Stream streamD = connection.Local().CreateStream(7, false);
        IHttp2Stream streamE = connection.Local().CreateStream(9, false);
        IHttp2Stream streamF = connection.Local().CreateStream(11, false);
        IHttp2Stream streamG = connection.Local().CreateStream(13, false);
        IHttp2Stream streamH = connection.Local().CreateStream(15, false);

        SetPriority(streamC.Id(), streamA.Id(), MAX_WEIGHT, false);
        SetPriority(streamD.Id(), streamA.Id(), MAX_WEIGHT, false);
        SetPriority(streamE.Id(), streamA.Id(), MAX_WEIGHT, false);

        SetPriority(streamF.Id(), streamB.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamG.Id(), streamB.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamH.Id(), streamB.Id(), 2 * DEFAULT_PRIORITY_WEIGHT, false);

        streamE.Close();
        // closing stream A will distribute its weight to the children (C & D) equally
        streamA.Close();
        // closing stream B will distribute its weight to the children (F & G & H) proportionally
        streamB.Close();
        // Level 0
        Assert.Equal(5, distributor.NumChildren(connection.ConnectionStream().Id()));
        // Level 1
        short halfWeight = DEFAULT_PRIORITY_WEIGHT / 2;
        Assert.True(distributor.IsChild(streamC.Id(), connection.ConnectionStream().Id(), halfWeight));
        Assert.True(distributor.IsChild(streamD.Id(), connection.ConnectionStream().Id(), halfWeight));

        short quarterWeight = DEFAULT_PRIORITY_WEIGHT / 4;
        Assert.True(distributor.IsChild(streamF.Id(), connection.ConnectionStream().Id(), quarterWeight));
        Assert.True(distributor.IsChild(streamG.Id(), connection.ConnectionStream().Id(), quarterWeight));
        Assert.True(distributor.IsChild(streamH.Id(), connection.ConnectionStream().Id(), (short) (2 * quarterWeight)));
    }

    [Fact]
    public void PriorityChangeWithNoPrioritizableDependentsShouldRestructureTree()
    {
        IHttp2Stream streamA = connection.Local().CreateStream(1, false);
        IHttp2Stream streamB = connection.Local().CreateStream(3, false);
        IHttp2Stream streamC = connection.Local().CreateStream(5, false);
        IHttp2Stream streamD = connection.Local().CreateStream(7, false);
        IHttp2Stream streamE = connection.Local().CreateStream(9, false);
        IHttp2Stream streamF = connection.Local().CreateStream(11, false);

        SetPriority(streamB.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamC.Id(), streamB.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamD.Id(), streamB.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamF.Id(), streamD.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        SetPriority(streamE.Id(), streamC.Id(), DEFAULT_PRIORITY_WEIGHT, false);

        // Leave leaf nodes open (E & F)
        streamA.Close();
        // Closing streamB will distribute its weight to the children (C & D) equally.
        streamB.Close();
        streamC.Close();
        streamD.Close();

        // Move F to depend on C, even though C is closed.
        SetPriority(streamF.Id(), streamC.Id(), DEFAULT_PRIORITY_WEIGHT, false);

        // Level 0
        Assert.Equal(2, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        short halfWeight = DEFAULT_PRIORITY_WEIGHT / 2;
        Assert.True(distributor.IsChild(streamE.Id(), connection.ConnectionStream().Id(), halfWeight));
        Assert.Equal(0, distributor.NumChildren(streamE.Id()));

        Assert.True(distributor.IsChild(streamF.Id(), connection.ConnectionStream().Id(), halfWeight));
        Assert.Equal(0, distributor.NumChildren(streamF.Id()));
    }

    [Fact]
    public void CircularDependencyShouldRestructureTree()
    {
        // Using example from https://tools.ietf.org/html/rfc7540#section-5.3.3
        // Initialize all the nodes
        IHttp2Stream streamA = connection.Local().CreateStream(1, false);
        IHttp2Stream streamB = connection.Local().CreateStream(3, false);
        IHttp2Stream streamC = connection.Local().CreateStream(5, false);
        IHttp2Stream streamD = connection.Local().CreateStream(7, false);
        IHttp2Stream streamE = connection.Local().CreateStream(9, false);
        IHttp2Stream streamF = connection.Local().CreateStream(11, false);

        Assert.Equal(6, distributor.NumChildren(connection.ConnectionStream().Id()));
        Assert.Equal(0, distributor.NumChildren(streamA.Id()));
        Assert.Equal(0, distributor.NumChildren(streamB.Id()));
        Assert.Equal(0, distributor.NumChildren(streamC.Id()));
        Assert.Equal(0, distributor.NumChildren(streamD.Id()));
        Assert.Equal(0, distributor.NumChildren(streamE.Id()));
        Assert.Equal(0, distributor.NumChildren(streamF.Id()));

        // Build the tree
        SetPriority(streamB.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        Assert.Equal(5, distributor.NumChildren(connection.ConnectionStream().Id()));
        Assert.True(distributor.IsChild(streamB.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(1, distributor.NumChildren(streamA.Id()));

        SetPriority(streamC.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        Assert.Equal(4, distributor.NumChildren(connection.ConnectionStream().Id()));
        Assert.True(distributor.IsChild(streamC.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(2, distributor.NumChildren(streamA.Id()));

        SetPriority(streamD.Id(), streamC.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        Assert.Equal(3, distributor.NumChildren(connection.ConnectionStream().Id()));
        Assert.True(distributor.IsChild(streamD.Id(), streamC.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(1, distributor.NumChildren(streamC.Id()));

        SetPriority(streamE.Id(), streamC.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        Assert.Equal(2, distributor.NumChildren(connection.ConnectionStream().Id()));
        Assert.True(distributor.IsChild(streamE.Id(), streamC.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(2, distributor.NumChildren(streamC.Id()));

        SetPriority(streamF.Id(), streamD.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        Assert.Equal(1, distributor.NumChildren(connection.ConnectionStream().Id()));
        Assert.True(distributor.IsChild(streamF.Id(), streamD.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(1, distributor.NumChildren(streamD.Id()));

        Assert.Equal(6, connection.NumActiveStreams());

        // Non-exclusive re-prioritization of a->d.
        SetPriority(streamA.Id(), streamD.Id(), DEFAULT_PRIORITY_WEIGHT, false);

        // Level 0
        Assert.Equal(1, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        Assert.True(distributor.IsChild(streamD.Id(), connection.ConnectionStream().Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(2, distributor.NumChildren(streamD.Id()));

        // Level 2
        Assert.True(distributor.IsChild(streamF.Id(), streamD.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamF.Id()));

        Assert.True(distributor.IsChild(streamA.Id(), streamD.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(2, distributor.NumChildren(streamA.Id()));

        // Level 3
        Assert.True(distributor.IsChild(streamB.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamB.Id()));

        Assert.True(distributor.IsChild(streamC.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(1, distributor.NumChildren(streamC.Id()));

        // Level 4
        Assert.True(distributor.IsChild(streamE.Id(), streamC.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamE.Id()));
    }

    [Fact]
    public void CircularDependencyWithExclusiveShouldRestructureTree()
    {
        // Using example from https://tools.ietf.org/html/rfc7540#section-5.3.3
        // Initialize all the nodes
        IHttp2Stream streamA = connection.Local().CreateStream(1, false);
        IHttp2Stream streamB = connection.Local().CreateStream(3, false);
        IHttp2Stream streamC = connection.Local().CreateStream(5, false);
        IHttp2Stream streamD = connection.Local().CreateStream(7, false);
        IHttp2Stream streamE = connection.Local().CreateStream(9, false);
        IHttp2Stream streamF = connection.Local().CreateStream(11, false);

        Assert.Equal(6, distributor.NumChildren(connection.ConnectionStream().Id()));
        Assert.Equal(0, distributor.NumChildren(streamA.Id()));
        Assert.Equal(0, distributor.NumChildren(streamB.Id()));
        Assert.Equal(0, distributor.NumChildren(streamC.Id()));
        Assert.Equal(0, distributor.NumChildren(streamD.Id()));
        Assert.Equal(0, distributor.NumChildren(streamE.Id()));
        Assert.Equal(0, distributor.NumChildren(streamF.Id()));

        // Build the tree
        SetPriority(streamB.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        Assert.Equal(5, distributor.NumChildren(connection.ConnectionStream().Id()));
        Assert.True(distributor.IsChild(streamB.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(1, distributor.NumChildren(streamA.Id()));

        SetPriority(streamC.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        Assert.Equal(4, distributor.NumChildren(connection.ConnectionStream().Id()));
        Assert.True(distributor.IsChild(streamC.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(2, distributor.NumChildren(streamA.Id()));

        SetPriority(streamD.Id(), streamC.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        Assert.Equal(3, distributor.NumChildren(connection.ConnectionStream().Id()));
        Assert.True(distributor.IsChild(streamD.Id(), streamC.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(1, distributor.NumChildren(streamC.Id()));

        SetPriority(streamE.Id(), streamC.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        Assert.Equal(2, distributor.NumChildren(connection.ConnectionStream().Id()));
        Assert.True(distributor.IsChild(streamE.Id(), streamC.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(2, distributor.NumChildren(streamC.Id()));

        SetPriority(streamF.Id(), streamD.Id(), DEFAULT_PRIORITY_WEIGHT, false);
        Assert.Equal(1, distributor.NumChildren(connection.ConnectionStream().Id()));
        Assert.True(distributor.IsChild(streamF.Id(), streamD.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(1, distributor.NumChildren(streamD.Id()));

        Assert.Equal(6, connection.NumActiveStreams());

        // Exclusive re-prioritization of a->d.
        SetPriority(streamA.Id(), streamD.Id(), DEFAULT_PRIORITY_WEIGHT, true);

        // Level 0
        Assert.Equal(1, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        Assert.True(distributor.IsChild(streamD.Id(), connection.ConnectionStream().Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(1, distributor.NumChildren(streamD.Id()));

        // Level 2
        Assert.True(distributor.IsChild(streamA.Id(), streamD.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(3, distributor.NumChildren(streamA.Id()));

        // Level 3
        Assert.True(distributor.IsChild(streamB.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamB.Id()));

        Assert.True(distributor.IsChild(streamF.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamF.Id()));

        Assert.True(distributor.IsChild(streamC.Id(), streamA.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(1, distributor.NumChildren(streamC.Id()));

        // Level 4;
        Assert.True(distributor.IsChild(streamE.Id(), streamC.Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamE.Id()));
    }

    // Unknown parent streams can come about in two ways:
    //  1. Because the stream is old and its state was purged
    //  2. This is the first reference to the stream, as implied at least by RFC7540§5.3.1:
    //    > A dependency on a stream that is not currently in the tree — such as a stream in the
    //    > "idle" state — results in that stream being given a default priority
    [Fact]
    public void UnknownParentShouldBeCreatedUnderConnection()
    {
        Setup(5);

        // Purposefully avoid creating streamA's Http2Stream so that is it completely unknown.
        // It shouldn't matter whether the ID is before or after streamB.id()
        int streamAId = 1;
        IHttp2Stream streamB = connection.Local().CreateStream(3, false);

        Assert.Equal(1, distributor.NumChildren(connection.ConnectionStream().Id()));
        Assert.Equal(0, distributor.NumChildren(streamB.Id()));

        // Build the tree
        SetPriority(streamB.Id(), streamAId, DEFAULT_PRIORITY_WEIGHT, false);

        Assert.Equal(1, connection.NumActiveStreams());

        // Level 0
        Assert.Equal(1, distributor.NumChildren(connection.ConnectionStream().Id()));

        // Level 1
        Assert.True(distributor.IsChild(streamAId, connection.ConnectionStream().Id(), DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(1, distributor.NumChildren(streamAId));

        // Level 2
        Assert.True(distributor.IsChild(streamB.Id(), streamAId, DEFAULT_PRIORITY_WEIGHT));
        Assert.Equal(0, distributor.NumChildren(streamB.Id()));
    }
}
