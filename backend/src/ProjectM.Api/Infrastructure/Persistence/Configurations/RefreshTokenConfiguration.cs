using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProjectM.Domain.Users;

namespace ProjectM.Api.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> e)
    {
        e.ToTable("RefreshTokens");
        e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
        e.Property(x => x.CreatedByIp).HasMaxLength(64);
        e.HasIndex(x => x.TokenHash).IsUnique();
        e.HasIndex(x => new { x.UserId, x.ExpiresAt });
        e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
