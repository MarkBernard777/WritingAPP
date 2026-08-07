namespace MasterBookWritingSystem.Core.Abstractions;

public sealed class InvalidProjectLocationException : Exception
{
    public InvalidProjectLocationException(string message)
        : base(message)
    {
    }
}

public sealed class ProjectAlreadyExistsException : Exception
{
    public ProjectAlreadyExistsException(string message)
        : base(message)
    {
    }
}

public sealed class ProjectNotFoundException : Exception
{
    public ProjectNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class ProjectValidationException : Exception
{
    public ProjectValidationException(string message)
        : base(message)
    {
    }
}
