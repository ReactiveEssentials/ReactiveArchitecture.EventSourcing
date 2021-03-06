﻿namespace Khala.EventSourcing.Azure
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using AutoFixture;
    using AutoFixture.AutoMoq;
    using FluentAssertions;
    using Khala.FakeDomain;
    using Microsoft.VisualStudio.TestTools.UnitTesting;
    using Moq;

    [TestClass]
    public class AzureEventSourcedRepository_specs
    {
        private IFixture _fixture;
        private IAzureEventStore _eventStore;
        private IAzureEventPublisher _eventPublisher;
        private IMementoStore _mementoStore;
        private AzureEventSourcedRepository<FakeUser> _sut;

        public TestContext TestContext { get; set; }

        [TestInitialize]
        public void TestInitialize()
        {
            _fixture = new Fixture().Customize(new AutoMoqCustomization());
            _eventStore = Mock.Of<IAzureEventStore>();
            _eventPublisher = Mock.Of<IAzureEventPublisher>();
            _mementoStore = Mock.Of<IMementoStore>();
            _sut = new AzureEventSourcedRepository<FakeUser>(
                _eventStore,
                _eventPublisher,
                _mementoStore,
                FakeUser.Factory,
                FakeUser.Factory);
        }

        [TestMethod]
        public void sut_implements_IEventSourcedRepositoryT()
        {
            _sut.Should().BeAssignableTo<IEventSourcedRepository<FakeUser>>();
        }

        [TestMethod]
        public void constructor_sets_EventPublisher_correctly()
        {
            IAzureEventPublisher eventPublisher = Mock.Of<IAzureEventPublisher>();

            var sut = new AzureEventSourcedRepository<FakeUser>(
                Mock.Of<IAzureEventStore>(),
                eventPublisher,
                FakeUser.Factory);

            sut.EventPublisher.Should().BeSameAs(eventPublisher);
        }

        [TestMethod]
        public async Task SaveAndPublish_saves_events()
        {
            // Arrange
            FakeUser user = _fixture.Create<FakeUser>();
            string operationId = _fixture.Create<string>();
            var correlationId = Guid.NewGuid();
            string contributor = _fixture.Create<string>();
            CancellationToken cancellationToken = new CancellationTokenSource().Token;
            user.ChangeUsername("foo");
            var pendingEvents = new List<IDomainEvent>(user.PendingEvents);

            // Act
            await _sut.SaveAndPublish(user, operationId, correlationId, contributor, cancellationToken);

            // Assert
            Mock.Get(_eventStore).Verify(
                x =>
                x.SaveEvents<FakeUser>(
                    pendingEvents,
                    operationId,
                    correlationId,
                    contributor,
                    cancellationToken),
                Times.Once());
        }

        [TestMethod]
        public async Task SaveAndPublish_publishes_events()
        {
            FakeUser user = _fixture.Create<FakeUser>();
            string operationId = _fixture.Create<string>();
            var correlationId = Guid.NewGuid();
            string contributor = _fixture.Create<string>();
            user.ChangeUsername("foo");

            await _sut.SaveAndPublish(user, operationId, correlationId, contributor);

            Mock.Get(_eventPublisher).Verify(
                x =>
                x.FlushPendingEvents<FakeUser>(
                    user.Id,
                    CancellationToken.None),
                Times.Once());
        }

        [TestMethod]
        public void SaveAndPublish_does_not_publish_events_if_fails_to_save()
        {
            // Arrange
            FakeUser user = _fixture.Create<FakeUser>();
            string operationId = _fixture.Create<string>();
            var correlationId = Guid.NewGuid();
            string contributor = _fixture.Create<string>();
            user.ChangeUsername("foo");
            Mock.Get(_eventStore)
                .Setup(
                    x =>
                    x.SaveEvents<FakeUser>(
                        It.IsAny<IEnumerable<IDomainEvent>>(),
                        It.IsAny<string>(),
                        It.IsAny<Guid?>(),
                        It.IsAny<string>(),
                        CancellationToken.None))
                .Throws<InvalidOperationException>();

            // Act
            Func<Task> action = () => _sut.SaveAndPublish(user, operationId, correlationId, contributor);

            // Assert
            action.ShouldThrow<InvalidOperationException>();
            Mock.Get(_eventPublisher).Verify(
                x =>
                x.FlushPendingEvents<FakeUser>(
                    user.Id,
                    CancellationToken.None),
                Times.Never());
        }

        [TestMethod]
        public async Task SaveAndPublish_saves_memento()
        {
            FakeUser user = _fixture.Create<FakeUser>();
            string operationId = _fixture.Create<string>();
            var correlationId = Guid.NewGuid();
            string contributor = _fixture.Create<string>();
            user.ChangeUsername("foo");

            await _sut.SaveAndPublish(user, operationId, correlationId, contributor);

            Mock.Get(_mementoStore).Verify(
                x =>
                x.Save<FakeUser>(
                    user.Id,
                    It.Is<FakeUserMemento>(
                        p =>
                        p.Version == user.Version &&
                        p.Username == user.Username),
                    CancellationToken.None),
                Times.Once());
        }

        [TestMethod]
        public void SaveAndPublish_does_not_save_memento_if_fails_to_save_events()
        {
            // Arrange
            FakeUser user = _fixture.Create<FakeUser>();
            string operationId = _fixture.Create<string>();
            var correlationId = Guid.NewGuid();
            string contributor = _fixture.Create<string>();
            user.ChangeUsername("foo");
            Mock.Get(_eventStore)
                .Setup(
                    x =>
                    x.SaveEvents<FakeUser>(
                        It.IsAny<IEnumerable<IDomainEvent>>(),
                        It.IsAny<string>(),
                        It.IsAny<Guid?>(),
                        It.IsAny<string>(),
                        CancellationToken.None))
                .Throws<InvalidOperationException>();

            // Act
            Func<Task> action = () => _sut.SaveAndPublish(user, operationId, correlationId, contributor);

            // Assert
            action.ShouldThrow<InvalidOperationException>();
            Mock.Get(_mementoStore).Verify(
                x =>
                x.Save<FakeUser>(
                        user.Id,
                        It.IsAny<IMemento>(),
                        It.IsAny<CancellationToken>()),
                Times.Never());
        }

        [TestMethod]
        public async Task Find_publishes_pending_events()
        {
            FakeUser user = _fixture.Create<FakeUser>();
            user.ChangeUsername("foo");

            await _sut.Find(user.Id, CancellationToken.None);

            Mock.Get(_eventPublisher).Verify(
                x =>
                x.FlushPendingEvents<FakeUser>(
                    user.Id,
                    CancellationToken.None),
                Times.Once());
        }

        [TestMethod]
        public async Task Find_restores_aggregate()
        {
            FakeUser user = _fixture.Create<FakeUser>();
            user.ChangeUsername("foo");
            Mock.Get(_eventStore)
                .Setup(x => x.LoadEvents<FakeUser>(user.Id, 0, CancellationToken.None))
                .ReturnsAsync(user.FlushPendingEvents());

            FakeUser actual = await _sut.Find(user.Id, CancellationToken.None);

            actual.ShouldBeEquivalentTo(user);
        }

        [TestMethod]
        public void Find_does_not_load_events_if_fails_to_publish_events()
        {
            // Arrange
            FakeUser user = _fixture.Create<FakeUser>();
            user.ChangeUsername("foo");
            Mock.Get(_eventPublisher)
                .Setup(
                    x =>
                    x.FlushPendingEvents<FakeUser>(
                        user.Id,
                        CancellationToken.None))
                .Throws<InvalidOperationException>();

            // Act
            Func<Task> action = () => _sut.Find(user.Id, CancellationToken.None);

            // Assert
            action.ShouldThrow<InvalidOperationException>();
            Mock.Get(_eventStore).Verify(
                x =>
                x.LoadEvents<FakeUser>(
                    user.Id,
                    It.IsAny<int>(),
                    It.IsAny<CancellationToken>()),
                Times.Never());
        }

        [TestMethod]
        public async Task Find_returns_null_if_event_not_found()
        {
            var userId = Guid.NewGuid();
            Mock.Get(_eventStore)
                .Setup(
                    x =>
                    x.LoadEvents<FakeUser>(userId, 0, CancellationToken.None))
                .ReturnsAsync(Enumerable.Empty<IDomainEvent>());

            FakeUser actual = await _sut.Find(userId, CancellationToken.None);

            actual.Should().BeNull();
        }

        [TestMethod]
        public async Task Find_restores_aggregate_using_memento_if_found()
        {
            // Arrange
            FakeUser user = _fixture.Create<FakeUser>();
            IMemento memento = user.SaveToMemento();
            user.ChangeUsername("foo");

            Mock.Get(_mementoStore)
                .Setup(x => x.Find<FakeUser>(user.Id, CancellationToken.None))
                .ReturnsAsync(memento);

            Mock.Get(_eventStore)
                .Setup(
                    x =>
                    x.LoadEvents<FakeUser>(user.Id, 1, CancellationToken.None))
                .ReturnsAsync(user.FlushPendingEvents().Skip(1))
                .Verifiable();

            // Act
            FakeUser actual = await _sut.Find(user.Id, CancellationToken.None);

            // Assert
            Mock.Get(_eventStore).Verify();
            actual.ShouldBeEquivalentTo(user);
        }
    }
}
