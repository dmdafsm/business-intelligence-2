using System.Text;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Platform.Persistence;

/// <summary>Maps columns, keys and indexes to snake_case so hand-written SQL needs no quoted identifiers.</summary>
public static class SnakeCaseNames
{
    public static void Apply(ModelBuilder builder)
    {
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(Convert(property.Name));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(Convert(key.GetName() ?? string.Empty));
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                foreignKey.SetConstraintName(Convert(foreignKey.GetConstraintName() ?? string.Empty));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(Convert(index.GetDatabaseName() ?? string.Empty));
            }
        }
    }

    public static string Convert(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (char.IsUpper(character))
            {
                if (index > 0 && name[index - 1] != '_' && (char.IsLower(name[index - 1]) || char.IsDigit(name[index - 1])
                    || (index + 1 < name.Length && char.IsLower(name[index + 1]))))
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
