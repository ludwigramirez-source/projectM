using FluentAssertions;
using Npgsql;
using ProjectM.Api.Infrastructure.Persistence;

namespace ProjectM.UnitTests;

public class ConnectionStringTests
{
    [Fact]
    public void Uri_de_Supabase_se_convierte_a_formato_Npgsql_con_SSL()
    {
        var cs = ConnectionStrings.NormalizePostgres(
            "postgresql://postgres.abcdefghijkl:Cl%40ve-123@aws-1-us-east-2.pooler.supabase.com:5432/postgres");
        var b = new NpgsqlConnectionStringBuilder(cs);
        b.Host.Should().Be("aws-1-us-east-2.pooler.supabase.com");
        b.Port.Should().Be(5432);
        b.Database.Should().Be("postgres");
        b.Username.Should().Be("postgres.abcdefghijkl");
        b.Password.Should().Be("Cl@ve-123");
        b.SslMode.Should().Be(SslMode.Require);
    }

    [Fact]
    public void Formato_clave_valor_se_deja_igual()
    {
        const string cs = "Host=db;Port=5432;Database=projectm;Username=postgres;Password=x";
        ConnectionStrings.NormalizePostgres(cs).Should().Be(cs);
    }

    [Fact]
    public void Respeta_sslmode_de_la_uri()
    {
        var cs = ConnectionStrings.NormalizePostgres("postgres://u:p@localhost/db?sslmode=disable");
        new NpgsqlConnectionStringBuilder(cs).SslMode.Should().Be(SslMode.Disable);
    }
}
