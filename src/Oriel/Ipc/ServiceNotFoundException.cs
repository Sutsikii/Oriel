namespace Oriel.Ipc;

/// <summary>A page called a service the app did not bind.</summary>
internal sealed class ServiceNotFoundException(string name) : Exception($"No service named {name} is bound");
