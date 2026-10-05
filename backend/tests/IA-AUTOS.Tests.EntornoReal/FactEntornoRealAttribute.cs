namespace IAAutos.Tests.EntornoReal;

/// <summary>
/// Prueba contra el entorno real. En local se omite si faltan las variables (para no romper "dotnet test" a quien no las tiene);
/// en GitHub Actions no se omite nunca: si faltan, la prueba falla con un mensaje claro.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FactEntornoRealAttribute : FactAttribute
{
    public FactEntornoRealAttribute()
    {
        var faltantes = clsEntornoReal.Faltantes();
        if (faltantes.Length > 0 && !clsEntornoReal.EsCi)
        {
            Skip = $"Entorno real no configurado (faltan: {string.Join(", ", faltantes)}).";
        }
    }
}
