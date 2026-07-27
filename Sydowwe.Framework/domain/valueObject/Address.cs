namespace Sydowwe.Framework.domain.valueObject;

public class Address
{
    public required string Street { get; set; }
    public required string HouseNumber { get; set; }
    public required string City { get; set; }
    public required string PostalCode { get; set; }
}