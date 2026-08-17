namespace Vectors.EuroScopeUpdater.Core.Model;

/// <summary>Which kind of AeroNav package a file represents.</summary>
public enum PackageType
{
    /// <summary>Full install package (clean install).</summary>
    Install,

    /// <summary>Update package — a strict subset that preserves personalization by omission.</summary>
    Update,
}
