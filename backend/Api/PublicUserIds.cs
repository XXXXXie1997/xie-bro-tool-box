using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using MySql.Data.MySqlClient;

namespace XieBox.Api;

public static class PublicUserIds
{
    // Public identifiers are labels, never credentials or authorization keys.
    public static async Task EnsureAsync(PlatformDb db, PlatformUser user)
    {
        if (user.PublicId is not null) return;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var candidate = RandomNumberGenerator.GetString("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 8);
            try
            {
                await db.Users.Where(x => x.Id == user.Id && x.PublicId == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.PublicId, candidate));
                user.PublicId = await db.Users.Where(x => x.Id == user.Id).Select(x => x.PublicId).SingleAsync();
                db.Entry(user).Property(x => x.PublicId).OriginalValue = user.PublicId;
                return;
            }
            catch (MySqlException error) when (error.Number == 1062) { /* Unique index collision: retry. */ }
        }
        throw new InvalidOperationException("Unable to allocate a public account identifier.");
    }
}
