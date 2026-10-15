namespace ProjectM.Domain.Projects;

public enum ProjectStatus
{
    Planeacion,
    Activo,
    EnPausa,
    EnCierre,
    Cerrado
}

public enum ProjectHealth
{
    EnTiempo,
    EnRiesgo,
    Retrasado
}

public enum RiskStatus
{
    Abierto,
    Mitigado,
    Cerrado
}
