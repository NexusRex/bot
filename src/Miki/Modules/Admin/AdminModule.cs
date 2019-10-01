namespace Miki.Modules.Admin
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading.Tasks;
    using Microsoft.EntityFrameworkCore;
    using Miki.Bot.Models;
    using Miki.Discord;
    using Miki.Discord.Common;
    using Miki.Discord.Common.Utils;
    using Miki.Discord.Rest;
    using Miki.Framework;
    using Miki.Framework.Commands;
    using Miki.Framework.Commands.Attributes;
    using Miki.Framework.Commands.Permissions;
    using Miki.Framework.Commands.Permissions.Attributes;
    using Miki.Framework.Commands.Permissions.Exceptions;
    using Miki.Framework.Commands.Permissions.Models;
    using Miki.Framework.Commands.Stages;
    using Miki.Localization;
    using Miki.Utility;

    [Module("Admin")]
    public class AdminModule
    {
        #region resource uris
        private const string PruneErrorNoMessages = "miki_module_admin_prune_no_messages";
        private const string PruneSuccess = "miki_module_admin_prune_success";
        #endregion
        [Command("ban")]
        [DefaultPermission(PermissionStatus.Deny)]
        public async Task BanAsync(IContext e)
        {
            IDiscordGuildUser currentUser = await e.GetGuild().GetSelfAsync();
            if ((await (e.GetChannel() as IDiscordGuildChannel).GetPermissionsAsync(currentUser)).HasFlag(GuildPermission.BanMembers))
            {
                e.GetArgumentPack().Take(out string userName);
                if (userName == null)
                {
                    return;
                }

                IDiscordGuildUser user = await DiscordExtensions.GetUserAsync(userName, e.GetGuild());

                if (user == null)
                {
                    await e.ErrorEmbed(e.GetLocale().GetString("ban_error_user_null"))
                        .ToEmbed().QueueAsync(e.GetChannel());
                    return;
                }

                IDiscordGuildUser author = await e.GetGuild()
                    .GetMemberAsync(e.GetAuthor().Id);

                if (await user.GetHierarchyAsync() >= await author.GetHierarchyAsync())
                {
                    await e.ErrorEmbed(e.GetLocale().GetString("permission_error_low", "ban")).ToEmbed()
                        .QueueAsync(e.GetChannel());
                    return;
                }

                if (await user.GetHierarchyAsync() >= await currentUser.GetHierarchyAsync())
                {
                    await e.ErrorEmbed(e.GetLocale().GetString("permission_error_low", "ban")).ToEmbed()
                        .QueueAsync(e.GetChannel());
                    return;
                }

                int prune = 1;
                if (e.GetArgumentPack().Take(out int pruneDays))
                {
                    prune = pruneDays;
                }

                string reason = e.GetArgumentPack().Pack.TakeAll();

                EmbedBuilder embed = new EmbedBuilder
                {
                    Title = "🛑 BAN",
                    Description = e.GetLocale().GetString("ban_header", $"**{e.GetGuild().Name}**")
                };

                if (!string.IsNullOrWhiteSpace(reason))
                {
                    embed.AddInlineField($"💬 {e.GetLocale().GetString("miki_module_admin_kick_reason")}", reason);
                }

                embed.AddInlineField(
                    $"💁 {e.GetLocale().GetString("miki_module_admin_kick_by")}",
                    $"{e.GetAuthor().Username}#{e.GetAuthor().Discriminator}");

                await embed.ToEmbed().SendToUser(user);

                await e.GetGuild().AddBanAsync(user, prune, reason);
            }
            else
            {
                await e.ErrorEmbed(e.GetLocale().GetString("permission_needed_error", $"`{e.GetLocale().GetString("permission_ban_members")}`"))
                    .ToEmbed().QueueAsync(e.GetChannel());
            }
        }

        [Command("clean")]
        [DefaultPermission(PermissionStatus.Deny)]
        public async Task CleanAsync(IContext e)
        {
            await PruneAsync(e, (await e.GetGuild().GetSelfAsync()).Id, null);
        }

        [Command("kick")]
        [DefaultPermission(PermissionStatus.Deny)]
        public async Task KickAsync(IContext e)
        {
            IDiscordGuildUser currentUser = await e.GetGuild().GetSelfAsync();
            var locale = e.GetLocale();

            if ((await (e.GetChannel() as IDiscordGuildChannel).GetPermissionsAsync(currentUser)).HasFlag(GuildPermission.KickMembers))
            {
                IDiscordGuildUser bannedUser;
                IDiscordGuildUser author = await e.GetGuild().GetMemberAsync(e.GetAuthor().Id);

                e.GetArgumentPack().Take(out string userName);

                bannedUser = await DiscordExtensions.GetUserAsync(userName, e.GetGuild());

                if (bannedUser == null)
                {
                    await e.ErrorEmbed(e.GetLocale().GetString("ban_error_user_null"))
                        .ToEmbed().QueueAsync(e.GetChannel());
                    return;
                }

                if (await bannedUser.GetHierarchyAsync() >= await author.GetHierarchyAsync())
                {
                    await e.ErrorEmbed(e.GetLocale().GetString("permission_error_low", "kick")).ToEmbed()
                        .QueueAsync(e.GetChannel());
                    return;
                }

                if (await bannedUser.GetHierarchyAsync() >= await currentUser.GetHierarchyAsync())
                {
                    await e.ErrorEmbed(e.GetLocale().GetString("permission_error_low", "kick")).ToEmbed()
                        .QueueAsync(e.GetChannel());
                    return;
                }

                string reason = "";
                if (e.GetArgumentPack().CanTake)
                {
                    reason = e.GetArgumentPack().Pack.TakeAll();
                }

                EmbedBuilder embed = new EmbedBuilder
                {
                    Title = locale.GetString("miki_module_admin_kick_header"),
                    Description = locale.GetString(
                        "miki_module_admin_kick_description", 
                        e.GetGuild().Name)
                };

                if (!string.IsNullOrWhiteSpace(reason))
                {
                    embed.AddField(locale.GetString("miki_module_admin_kick_reason"), reason, true);
                }

                embed.AddField(
                    locale.GetString("miki_module_admin_kick_by"),
                    $"{author.Username}#{author.Discriminator}", 
                    true);

                embed.Color = new Color(1, 1, 0);

                await embed.ToEmbed()
                    .SendToUser(bannedUser);
                await bannedUser.KickAsync(reason);
            }
            else
            {
                await e.ErrorEmbed(
                        e.GetLocale().GetString("permission_needed_error", $"`{e.GetLocale().GetString("permission_kick_members")}`"))
                    .ToEmbed().QueueAsync(e.GetChannel());
            }
        }

        [Command("prune")]
        [DefaultPermission(PermissionStatus.Deny)]
        public async Task PruneAsync(IContext e)
        {
            await PruneAsync(e, 0, null);
        }

        public async Task PruneAsync(IContext e, ulong target = 0, string filter = null)
        {
            IDiscordGuildUser invoker = await e.GetGuild()
                .GetSelfAsync();
            var locale = e.GetLocale();

            if (!(await (e.GetChannel() as IDiscordGuildChannel).GetPermissionsAsync(invoker)).HasFlag(GuildPermission.ManageMessages))
            {
                e.GetChannel()
                    .QueueMessage(e.GetLocale().GetString("miki_module_admin_prune_error_no_access"));
                return;
            }

            if (e.GetArgumentPack().Pack.Length < 1)
            {
                await new EmbedBuilder()
                    .SetTitle("♻ Prune")
                    .SetColor(119, 178, 85)
                    .SetDescription(e.GetLocale().GetString("miki_module_admin_prune_no_arg"))
                    .ToEmbed()
                    .QueueAsync(e.GetChannel());
                return;
            }


            string args = e.GetArgumentPack().Pack.TakeAll();
            string[] argsSplit = args.Split(' ');
            target = e.GetMessage().MentionedUserIds.Count > 0
                ? (await e.GetGuild().GetMemberAsync(e.GetMessage().MentionedUserIds.First())).Id
                : target;

            if (int.TryParse(argsSplit[0], out int amount))
            {
                if (amount < 0)
                {
                    await Utils.ErrorEmbed(e, locale.GetString("miki_module_admin_prune_error_negative"))
                        .ToEmbed().QueueAsync(e.GetChannel());
                    return;
                }
                if (amount > 100)
                {
                    await Utils.ErrorEmbed(e, locale.GetString("miki_module_admin_prune_error_max"))
                        .ToEmbed().QueueAsync(e.GetChannel());
                    return;
                }
            }
            else
            {
                await Utils.ErrorEmbed(e, locale.GetString("miki_module_admin_prune_error_parse"))
                    .ToEmbed().QueueAsync(e.GetChannel());
                return;
            }

            if (Regex.IsMatch(e.GetArgumentPack().Pack.TakeAll(), "\"(.*?)\""))
            {
                Regex regex = new Regex("\"(.*?)\"");
                filter = regex.Match(e.GetArgumentPack().Pack.TakeAll()).ToString().Trim('"', ' ');
            }

            await e.GetMessage()
                .DeleteAsync(); // Delete the calling message before we get the message history.

            IEnumerable<IDiscordMessage> messages = await e.GetChannel()
                .GetMessagesAsync(amount);
            List<IDiscordMessage> deleteMessages = new List<IDiscordMessage>();

            amount = messages.Count();

            if (amount < 1)
            {
                await e.GetMessage()
                    .DeleteAsync();

                await e.ErrorEmbed(locale.GetString(PruneErrorNoMessages, e.GetPrefixMatch()))
                    .ToEmbed()
                    .QueueAsync(e.GetChannel());
                return;
            }
            for (int i = 0; i < amount; i++)
            {
                if (target != 0 && messages.ElementAt(i)?.Author.Id != target)
                    continue;

                if (filter != null && messages.ElementAt(i)?.Content.IndexOf(filter) < 0)
                    continue;

                if (messages.ElementAt(i).Timestamp.AddDays(14) > DateTime.Now)
                {
                    deleteMessages.Add(messages.ElementAt(i));
                }
            }

            if (deleteMessages.Count > 0)
            {
                await e.GetChannel()
                    .DeleteMessagesAsync(deleteMessages.ToArray());
            }

            await e.SuccessEmbedResource(PruneSuccess, deleteMessages.Count)
                .QueueAsync(e.GetChannel())
                .ThenWaitAsync(5000)
                .ThenDeleteAsync();
        }

        [Command("permissions")]
        public class PermissionsCommand
        {
            private const string PermissionSet = "permission_set";

            [Command("allow")]
            [RequiresPipelineStage(typeof(PermissionPipelineStage))]
            [DefaultPermission(PermissionStatus.Deny)]
            public Task AllowPermissionsAsync(IContext e)
            {
                return SetPermissionsAsync(e, PermissionStatus.Allow);
            }

            [Command("deny")]
            [RequiresPipelineStage(typeof(PermissionPipelineStage))]
            [DefaultPermission(PermissionStatus.Deny)]
            public Task DenyPermissionsAsync(IContext e)
            {
                return SetPermissionsAsync(e, PermissionStatus.Deny);
            }

            [Command("reset")]
            [RequiresPipelineStage(typeof(PermissionPipelineStage))]
            [DefaultPermission(PermissionStatus.Deny)]
            public Task ResetPermissionsAsync(IContext e)
            {
                return SetPermissionsAsync(e, PermissionStatus.Default);
            }

            [Command("list")]
            [RequiresPipelineStage(typeof(PermissionPipelineStage))]
            [DefaultPermission(PermissionStatus.Deny)]
            public async Task ListPermissionsAsync(IContext e)
            {
                var db = e.GetService<DbContext>();
                var permissions = e.GetService<PermissionService>();

                List<long> idList = new List<long>();
                if(e.GetAuthor() is IDiscordGuildUser gm)
                {
                    idList.AddRange(gm.RoleIds.Select(x => (long)x));
                }
                idList.Add((long)e.GetGuild().Id);
                idList.Add((long)e.GetChannel().Id);
                idList.Add((long)e.GetAuthor().Id);

                var allPermissions = await permissions.ListPermissionsAsync(
                    (long)e.GetGuild().Id, idList.ToArray());
                if(!allPermissions.Any())
                {
                    await e.GetChannel()
                        .SendMessageAsync("empty");
                    return;
                }

                await new EmbedBuilder()
                    .SetTitle("⚡ Your permissions")
                    .SetColor(180, 180, 90)
                    .SetDescription(
                        string.Join(
                            "\n", allPermissions.Select(x
                                => $"{GetStatusEmoji(x.Status)} {x.CommandName} for {x.Type} {x.EntityId}")))
                    .ToEmbed()
                    .QueueAsync(e.GetChannel());
            }

            private string GetStatusEmoji(PermissionStatus status)
            {
                switch (status)
                {
                    case PermissionStatus.Allow:
                        return "✅";
                    case PermissionStatus.Default:
                    case PermissionStatus.Deny:
                        return "❌";
                }

                return "";
            }

            private class Entity
            {
                public long Id { get; set; }
                public string Resource { get; set; }
                public EntityType Type { get; set; }
            }

            private async Task SetPermissionsAsync(IContext e, PermissionStatus level)
            {
                var permissions = e.GetService<PermissionService>();
                var commands = e.GetStage<CommandHandlerStage>();

                var db = e.GetService<DbContext>();

                if(!e.GetArgumentPack().Take(out string commandName))
                {
                    return;
                }
                var command = commands.GetCommand(commandName.Replace('.', ' '));
                if(!(command is IExecutable executable))
                {
                    //invalid command
                    return;
                }

                var ownPermission = await permissions.GetPriorityPermissionAsync(e);
                if(ownPermission.Status == PermissionStatus.Deny)
                {
                    throw new PermissionUnauthorizedException();
                }

                Entity entity = await GetEntityAsync(e);

                await permissions.SetPermissionAsync(new Permission
                {
                    EntityId = entity.Id,
                    Type = entity.Type,
                    CommandName = command.ToString(),
                    Status = level,
                    GuildId = (long)e.GetGuild().Id
                });
                await db.SaveChangesAsync();

                await e.SuccessEmbedResource(PermissionSet, entity.Resource, level)
                    .QueueAsync(e.GetChannel());
            }

            private async ValueTask<Entity> GetEntityAsync(IContext e)
            {
                if(!e.GetArgumentPack().Take(out string type))
                {
                    return null;
                }

                if(Enum.TryParse<EntityType>(type, true, out var entityType))
                {
                    return await GetEntityFromType(e, entityType);
                }

                if(Mention.TryParse(type, out var mention))
                {
                    return await GetEntityFromMention(e, mention);
                }
                return null;
            }

            private async ValueTask<Entity> GetEntityFromType(IContext e, EntityType type)
            {
                var entity = new Entity
                {
                    Type = type
                };
                switch(type)
                {
                    case EntityType.User:
                    {
                        if(!e.GetArgumentPack().Take(out string resource))
                        {
                            return null;
                        }
                        var user = await e.GetGuild().FindUserAsync(resource);
                        if(user == null)
                        {
                            throw new InvalidEntityException();
                        }

                        entity.Id = (long)user.Id;
                        entity.Resource = user.Username;
                    }
                    break;

                    case EntityType.Channel:
                    {
                        if(!e.GetArgumentPack().Take(out string resource))
                        {
                            return null;
                        }

                        var channel = await e.GetGuild().FindChannelAsync(resource);
                        if(channel == null)
                        {
                            throw new InvalidEntityException();
                        }
                        entity.Id = (long)channel.Id;
                        entity.Resource = (await e.GetGuild().GetChannelAsync(channel.Id)).Name;
                    }
                    break;

                    case EntityType.Role:
                    {
                        if(!e.GetArgumentPack().Take(out string resource))
                        {
                            return null;
                        }

                        var role = await e.GetGuild().FindRoleAsync(resource);
                        if (role == null)
                        {
                            throw new InvalidEntityException();
                        }
                        entity.Id = (long)role.Id;
                        entity.Resource = role.Name;
                    }
                    break;

                    case EntityType.Guild:
                    {
                        entity = new Entity
                        {
                            Id = (long)e.GetGuild().Id,
                            Resource = e.GetGuild().Name
                        };
                    }
                    break;

                    case EntityType.Global:
                    {
                        entity = new Entity
                        {
                            Id = 0L,
                            Resource = "globally"
                        };
                    }
                    break;

                    default:
                    {
                        throw new NotSupportedException();
                    }
                }
                return entity;
            }

            private async ValueTask<Entity> GetEntityFromMention(IContext e, Mention mention)
            {
                var entity = new Entity
                {
                    Id = (long) mention.Id
                };

                switch (mention.Type)
                {
                    case MentionType.USER:
                    case MentionType.USER_NICKNAME:
                    {
                        var user = await e.GetGuild().GetMemberAsync(mention.Id);
                        if (user == null)
                        {
                            throw new InvalidEntityException();
                        }

                        entity.Resource = user.Username;
                        entity.Type = EntityType.User;
                    } break;

                    case MentionType.CHANNEL:
                    {
                        var channel = await e.GetGuild().GetChannelAsync(mention.Id);
                        if(channel == null)
                        {
                            throw new InvalidEntityException();
                        }

                        entity.Resource = channel.Name;
                        entity.Type = EntityType.User;
                    } break;

                    case MentionType.ROLE:
                    {
                        var role = await e.GetGuild().GetRoleAsync(mention.Id);
                        if(role == null)
                        {
                            throw new InvalidEntityException();
                        }

                        entity.Resource = role.Name;
                        entity.Type = EntityType.User;
                    } break;

                    default:
                        throw new NotSupportedException();
                }
                return entity;
            }
        }

        [Command("setevent", "setcommand")]
        [DefaultPermission(PermissionStatus.Deny)]
        public async Task SetCommandAsync(IContext e)
        {
            if (!e.GetArgumentPack().Take(out string commandId))
            {
                // require command argument
                return;
            }

            commandId = commandId.Replace('.', ' ');

            var handler = e.GetStage<CommandHandlerStage>();

            var command = handler.GetCommand(commandId);
            if (command == null)
            {
                await e.ErrorEmbed($"'{commandId}' is not a valid command")
                    .ToEmbed().QueueAsync(e.GetChannel());
                return;
            }

            if (!e.GetArgumentPack().Take(out bool setValue))
            {
                return;
            }

            string localeState = (setValue)
                ? e.GetLocale().GetString("miki_generic_enabled")
                : e.GetLocale().GetString("miki_generic_disabled");

            bool global = false;

            var context = e.GetService<MikiDbContext>();

            if (e.GetArgumentPack().Peek(out string g))
            {
                if (g == "-g")
                {
                    global = true;
                    var channels = await e.GetGuild().GetChannelsAsync();
                    foreach (var c in channels)
                    {
                        // TODO: implement in a better way.
                    }
                }
            }

            await context.SaveChangesAsync();

            string outputDesc = localeState + " " + commandId;
            if (global)
            {
                outputDesc += " in every channel";
            }

            await e.SuccessEmbed(outputDesc)
                .QueueAsync(e.GetChannel());
        }

        [Command("softban")]
        public async Task SoftbanAsync(IContext e)
        {
            IDiscordGuildUser currentUser = await e.GetGuild().GetSelfAsync();
            if ((await (e.GetChannel() as IDiscordGuildChannel).GetPermissionsAsync(currentUser)).HasFlag(GuildPermission.BanMembers))
            {
                if (!e.GetArgumentPack().Take(out string argObject))
                {
                    return;
                }

                IDiscordGuildUser user = await DiscordExtensions.GetUserAsync(argObject, e.GetGuild());
                if (user == null)
                {
                    await e.ErrorEmbed(e.GetLocale().GetString("ban_error_user_null"))
                        .ToEmbed().QueueAsync(e.GetChannel());
                    return;
                }

                string reason = null;
                if (e.GetArgumentPack().CanTake)
                {
                    reason = e.GetArgumentPack()
                        .Pack.TakeAll();
                }

                IDiscordGuildUser author = await e.GetGuild().GetMemberAsync(e.GetAuthor().Id);

                if (await user.GetHierarchyAsync() >= await author.GetHierarchyAsync())
                {
                    await e.ErrorEmbed(e.GetLocale().GetString("permission_error_low", "softban")).ToEmbed()
                        .QueueAsync(e.GetChannel());
                    return;
                }

                if (await user.GetHierarchyAsync() >= await currentUser.GetHierarchyAsync())
                {
                    await e.ErrorEmbed(e.GetLocale().GetString("permission_error_low", "softban")).ToEmbed()
                        .QueueAsync(e.GetChannel());
                    return;
                }

                EmbedBuilder embed = new EmbedBuilder
                {
                    Title = "⚠ SOFTBAN",
                    Description = $"You've been banned from **{e.GetGuild().Name}**!"
                };

                if (!string.IsNullOrWhiteSpace(reason))
                {
                    embed.AddInlineField("💬 Reason", reason);
                }

                embed.AddInlineField(
                    "💁 Banned by",
                    $"{author.Username}#{author.Discriminator}");

                await embed.ToEmbed()
                    .SendToUser(user);

                await e.GetGuild().AddBanAsync(user, 1, reason);
                await e.GetGuild().RemoveBanAsync(user);
            }
            else
            {
                await e.ErrorEmbed(e.GetLocale().GetString(
                    "permission_needed_error",
                    $"`{e.GetLocale().GetString("permission_ban_members")}`"))
                    .ToEmbed().QueueAsync(e.GetChannel());
            }
        }
    }

    internal class InvalidEntityException : Exception
    {
    }
}