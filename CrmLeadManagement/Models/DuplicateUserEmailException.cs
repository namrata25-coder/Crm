namespace CrmLeadManagement.Models;

public sealed class DuplicateUserEmailException : Exception
{
    public DuplicateUserEmailException(string email)
        : base($"A user with email '{email}' already exists.")
    {
    }

    public DuplicateUserEmailException(string email, Exception innerException)
        : base($"A user with email '{email}' already exists.", innerException)
    {
    }
}
