﻿using System;
using System.Linq;
using System.Net;
using System.Threading;
using SlackAPI.Tests.Configuration;
using SlackAPI.Tests.Helpers;
using SlackAPI.WebSocketMessages;
using Xunit;

namespace SlackAPI.Tests
{
    [Collection("Integration tests")]
    public class Connect
    {
        const string TestText = "Test :D";
        private readonly IntegrationFixture fixture;

        public Connect(IntegrationFixture fixture)
        {
            this.fixture = fixture;
        }

        [Fact]
        public void TestConnectAsUser()
        {
            var client = this.fixture.CreateUserClient();
            Assert.True(client.IsConnected, "Invalid, doesn't think it's connected.");
            client.CloseSocket();
        }

        [Fact]
        public void TestConnectAsBot()
        {
            var client = this.fixture.CreateBotClient();
            Assert.True(client.IsConnected, "Invalid, doesn't think it's connected.");
            client.CloseSocket();
        }

        [Fact]
        public void TestConnectWithWrongProxySettings()
        {
            var proxySettings = new WebProxy { Address = new Uri("http://127.0.0.1:8080")};
            Assert.Throws<InvalidOperationException>(() => this.fixture.CreateUserClient(proxySettings));
            Assert.Throws<InvalidOperationException>(() => this.fixture.CreateBotClient(proxySettings));
        }

        [Fact]
        public void TestConnectPostAndDelete()
        {
            // given
            SlackSocketClient client = this.fixture.CreateUserClient();
            string channel = this.fixture.Config.TestChannel;

            // when
            DateTime messageTimestamp = PostMessage(client, channel);
            DeletedResponse deletedResponse = DeleteMessage(client, channel, messageTimestamp);

            // then
            Assert.NotNull(deletedResponse);
            Assert.True(deletedResponse.ok);
            Assert.Equal(channel, deletedResponse.channel);
            Assert.Equal(messageTimestamp, deletedResponse.ts);
        }

        [Fact]
        public void TestConnectGetPresenceChanges()
        {
            // Arrange
            SlackSocketClient client;
            int presenceChangesRaisedCount = 0;
            using (var sync = new InSync(nameof(TestConnectGetPresenceChanges), this.fixture.ConnectionTimeout))
            {
                void OnPresenceChanged(SlackSocketClient sender, PresenceChange e)
                {
                    if (++presenceChangesRaisedCount == sender.Users.Count)
                    {
                        sync.Proceed();
                    }
                }

                // Act
                client = this.fixture.CreateUserClient(maintainPresenceChangesStatus: true, presenceChanged: OnPresenceChanged);
            }

            // Assert
            Assert.True(client.Users.All(x => x.presence != null));
        }

        [Fact]
        public void TestManualSubscribePresenceChangeAndManualPresenceChange()
        {
            // Arrange
            SlackSocketClient client;
            using (var sync = new InSync(nameof(TestConnectGetPresenceChanges), this.fixture.ConnectionTimeout))
            {
                void OnPresenceChanged(SlackSocketClient sender, PresenceChange e)
                {
                    if (e.user == sender.MySelf.id)
                    {
                        // Assert
                        sync.Proceed();
                    }
                }

                client = this.fixture.CreateUserClient(presenceChanged: OnPresenceChanged);

                // Act
                client.SubscribePresenceChange(client.MySelf.id);
            }

            // Set initial state
            using (var sync = new InSync(nameof(TestConnectGetPresenceChanges)))
            {
                client.EmitPresence(p => sync.Proceed(), Presence.active);
            }

            using (var sync = new InSync(nameof(TestConnectGetPresenceChanges)))
            {
                client.OnPresenceChanged += x =>
                {
                    if (x is ManualPresenceChange && x.user == client.MySelf.id)
                    {
                        // Assert
                        sync.Proceed();
                    }
                };

                // Act
                client.EmitPresence(x => { }, Presence.away);
            }
        }

        private static DateTime PostMessage(SlackSocketClient client, string channel)
        {
            MessageReceived sendMessageResponse = null;

            using (var sync = new InSync(nameof(SlackSocketClient.SendMessage)))
            {
                client.SendMessage(response =>
                {
                    sendMessageResponse = response;
                    sync.Proceed();
                }, channel, TestText);
            }

            Assert.NotNull(sendMessageResponse);
            Assert.Equal(TestText, sendMessageResponse.text);

            return sendMessageResponse.ts;
        }

        private static DeletedResponse DeleteMessage(SlackSocketClient client, string channel, DateTime messageTimestamp)
        {
            DeletedResponse deletedResponse = null;

            using (var sync = new InSync(nameof(SlackClient.DeleteMessage)))
            {
                client.DeleteMessage(response =>
                {
                    deletedResponse = response;
                    sync.Proceed();
                }, channel, messageTimestamp);
            }

            return deletedResponse;
        }
    }
}
