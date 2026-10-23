using FluentAssertions;
using Npgsql;
using ProjectM.Api.Infrastructure.Persistence;

namespace ProjectM.UnitTests;

public class ConnectionStringTests
{
    private const string SupabaseUri =
        "postgresql://postgres.abcdefghijkl:Cl%40ve-123@aws-1-us-east-2.pooler.supabase.com:5432/postgres";

    [Fact]
    public void Uri_de_Supabase_se_convierte_a_formato_Npgsql()
    {
        var b = new NpgsqlConnectionStringBuilder(ConnectionStrings.NormalizePostgres(SupabaseUri));
        b.Host.Should().Be("aws-1-us-east-2.pooler.supabase.com");
        b.Port.Should().Be(5432);
        b.Database.Should().Be("postgres");
        b.Username.Should().Be("postgres.abcdefghijkl");
        b.Password.Should().Be("Cl@ve-123");
    }

    [Fact]
    public void Supabase_usa_VerifyFull_con_la_CA_incluida_en_la_imagen()
    {
        var b = new NpgsqlConnectionStringBuilder(ConnectionStrings.NormalizePostgres(SupabaseUri));
        b.SslMode.Should().Be(SslMode.VerifyFull);
        b.RootCertificate.Should().EndWith("supabase-prod-ca-2021.crt");
        File.Exists(b.RootCertificate).Should().BeTrue("la CA se copia al directorio de salida");
    }

    [Fact]
    public void Con_CA_configurada_cualquier_host_remoto_usa_VerifyFull()
    {
        var cs = ConnectionStrings.NormalizePostgres("Host=pg.ejemplo.com;Database=app;Username=u;Password=p", "/certs/ca.crt");
        var b = new NpgsqlConnectionStringBuilder(cs);
        b.SslMode.Should().Be(SslMode.VerifyFull);
        b.RootCertificate.Should().Be("/certs/ca.crt");
    }

    [Theory]
    [InlineData("Host=pg.ejemplo.com;Database=app;Username=u;Password=p")]
    [InlineData("Host=pg.ejemplo.com;Database=app;Username=u;Password=p;SSL Mode=Disable")]
    [InlineData("Host=pg.ejemplo.com;Database=app;Username=u;Password=p;SSL Mode=Prefer")]
    [InlineData("postgresql://u:p@pg.ejemplo.com/app?sslmode=disable")]
    public void Host_remoto_nunca_baja_de_Require(string input)
    {
        var b = new NpgsqlConnectionStringBuilder(ConnectionStrings.NormalizePostgres(input));
        b.SslMode.Should().Be(SslMode.Require);
    }

    [Fact]
    public void Uri_remota_puede_subir_a_verify_full()
    {
        var cs = ConnectionStrings.NormalizePostgres("postgresql://u:p@pg.ejemplo.com/app?sslmode=verify-full");
        new NpgsqlConnectionStringBuilder(cs).SslMode.Should().Be(SslMode.VerifyFull);
    }

    [Fact]
    public void Host_local_de_Compose_se_deja_igual()
    {
        const string cs = "Host=db;Port=5432;Database=projectm;Username=postgres;Password=x";
        ConnectionStrings.NormalizePostgres(cs).Should().Be(cs);
    }

    [Fact]
    public void Uri_local_respeta_su_sslmode()
    {
        var cs = ConnectionStrings.NormalizePostgres("postgres://u:p@localhost/db?sslmode=disable");
        new NpgsqlConnectionStringBuilder(cs).SslMode.Should().Be(SslMode.Disable);
    }
}
