namespace MechanicsSoftware.Application.Exceptions;

public sealed class BillingUnavailableException(string message, Exception? innerException = null)
    : Exception(message, innerException);
