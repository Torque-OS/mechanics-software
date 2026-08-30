using MechanicsSoftware.Domain.Enums;
using MechanicsSoftware.Domain.Exceptions;
using MechanicsSoftware.Domain.ValueObjects;

namespace MechanicsSoftware.Domain.Entities;

public sealed class Customer : Entity<Guid>
{
    public string Name { get; private set; } = null!;
    public TaxId Document { get; private set; } = null!;
    public Email Email { get; private set; } = null!;
    public string Phone { get; private set; } = null!;
    public bool Active { get; private set; } = true;

    private Customer() { }

    private Customer(Guid id, string name, TaxId taxId, Email email, string phone)
    {
        Id = id;
        Name = name;
        Document = taxId;
        Email = email;
        Phone = phone;
    }

    public static Customer Create(Guid id, string name, string taxId, PersonType personType, string email, string phone)
    {
        ValidateName(name);
        ValidatePhone(phone);

        return new Customer(
            id,
            name.Trim(),
            new TaxId(taxId, personType),
            new Email(email),
            phone.Trim()
        );
    }

    public void Update(string name, string email, string phone)
    {
        ValidateName(name);
        ValidatePhone(phone);

        Name = name.Trim();
        Email = new Email(email);
        Phone = phone.Trim();
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Customer name is required.");

        if (name.Trim().Length > 100)
            throw new DomainException("Customer name must not exceed 100 characters.");
    }

    private static void ValidatePhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            throw new DomainException("Phone is required.");

        var digits = new string(phone.Where(char.IsDigit).ToArray());

        if (digits.Length < 8 || digits.Length > 15)
            throw new DomainException("Phone must have between 8 and 15 digits (with area code).");
    }
}
