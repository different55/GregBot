﻿using Discord.WebSocket;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Timers;
using System.Threading.Tasks;
using Discord;

namespace ForumCrawler
{
    public static class MuteWatcher
    {
        private static event Func<Mute, string, Task> OnMute;
        private static event Func<ulong, Task> OnUnmute;
        public static void Bind(DiscordSocketClient client)
        {
            OnMute += (a, b) => MuteWatcher_OnMute(client, a, b);
            OnUnmute += mute => MuteWatcher_OnUnmute(client, mute);
            client.Ready += () => Client_Ready(client);
            client.UserJoined += VerifyMute;
            client.GuildMemberUpdated += (oldUser, newUser) => Client_GuildMemberUpdated(client, oldUser, newUser);
        }

        private static async Task VerifyMute(SocketGuildUser user)
        {
            var mute = await Database.GetMute(user.Id);
            if (mute != null && mute.ExpiryDate > DateTime.UtcNow)
            {
                await OnMute(mute, "Mute retention after rejoin.");
            }
        }

        private static async Task Client_GuildMemberUpdated(DiscordSocketClient client, SocketGuildUser oldUser, SocketGuildUser newUser)
        {
            var role = client.GetGuild(DiscordSettings.GuildId).GetRole(DiscordSettings.MutedRole);
            if (oldUser.Roles.Contains(role) && !newUser.Roles.Contains(role))
            {
                await VerifyMute(newUser);
            }
        }

        private static Task Client_Ready(DiscordSocketClient client)
        {
            var timer = new Timer(TimeSpan.FromMinutes(1).TotalMilliseconds);
            timer.Elapsed += (o, e) => OnUpdate(client);
            timer.Start();
            return Task.CompletedTask;
        }

        private static async Task MuteWatcher_OnMute(DiscordSocketClient client, Mute mute, string reason)
        {
            var user = client.GetGuild(DiscordSettings.GuildId).GetUser(mute.UserId);
            await user.AddRoleAsync(client.GetGuild(DiscordSettings.GuildId).GetRole(DiscordSettings.MutedRole));
            await user.SendMessageAsync($"You were muted until {mute.ExpiryDate} UTC by {MentionUtils.MentionUser(mute.IssuerId)}. Reason: {reason}");
        }

        private static async Task MuteWatcher_OnUnmute(DiscordSocketClient client, ulong userId)
        {
            var role = client.GetGuild(DiscordSettings.GuildId).GetRole(DiscordSettings.MutedRole);
            var user = client.GetGuild(DiscordSettings.GuildId).GetUser(userId);
            if (user.Roles.Contains(role))
            {
                await user.RemoveRoleAsync(role);
            }
        }

        private static async void OnUpdate(DiscordSocketClient client)
        {
            var timestamp = DateTime.UtcNow;
            var mutes = await Database.GetAllExpiredMutes(timestamp);
            foreach (var mute in mutes)
            {
                await OnUnmute(mute.UserId);
            }
            await Database.RemoveAllExpiredMutes(timestamp);
        }

        public static async Task<Mute> MuteUser(Mute mute, string reason, bool shorten, bool sameAuthorShorten)
        {
            if (mute.ExpiryDate <= DateTime.UtcNow) return null;
            var lastMute = await Database.GetMute(mute.UserId);
            var shorts = mute.ExpiryDate <= lastMute?.ExpiryDate;
            var sameAuthor = lastMute?.IssuerId == mute.IssuerId;
            if ((shorts && !shorten) || (shorts && !sameAuthor && sameAuthorShorten))
            {
                return lastMute;
            }

            await Database.AddOrUpdateMuteAsync(mute);
            await OnMute(mute, reason);
            return mute;
        }

        public static async Task UnmuteUser(ulong userId, ulong? issuerId)
        {
            if (issuerId != null)
            {
                var lastMute = await Database.GetMute(userId);
                if (lastMute == null || lastMute.IssuerId != issuerId) return;
            }

            await Database.RemoveMute(userId);
            await OnUnmute(userId);
        }
    }
}