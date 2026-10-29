namespace EBoekhouden.Api.Contracts;

public sealed record RelationSummaryDto
{
    public required int Id { get; init; }
    public string? Type { get; init; }
    public string? Code { get; init; }
}

public sealed record RelationDto
{
    public required int Id { get; init; }
    public string? Type { get; init; }
    public string? Code { get; init; }
    public DateOnly? Date { get; init; }
    public string? Name { get; init; }
    public string? Salutation { get; init; }
    public string? Contact { get; init; }
    public string? Gender { get; init; }
    public string? Address { get; init; }
    public string? PostalCode { get; init; }
    public string? City { get; init; }
    public string? Country { get; init; }
    public string? Address2 { get; init; }
    public string? PostalCode2 { get; init; }
    public string? City2 { get; init; }
    public string? Country2 { get; init; }
    public string? PhoneNumber { get; init; }
    public string? MobilePhoneNumber { get; init; }
    public string? FaxNumber { get; init; }
    public string? EmailAddress { get; init; }
    public string? EmailAddressInvoice { get; init; }
    public string? EmailAddressReminder { get; init; }
    public string? Website { get; init; }
    public string? Note { get; init; }
    public string? VatNumber { get; init; }
    public bool? Inactive { get; init; }
    public int? TermOfPayment { get; init; }
    public string? CompanyRegistrationNumber { get; init; }
    public string? Iban { get; init; }
    public string? Bic { get; init; }
    public string? FreeText1 { get; init; }
    public string? FreeText2 { get; init; }
    public string? FreeText3 { get; init; }
    public string? FreeText4 { get; init; }
    public string? FreeText5 { get; init; }
    public string? FreeText6 { get; init; }
    public string? FreeText7 { get; init; }
    public string? FreeText8 { get; init; }
    public string? FreeText9 { get; init; }
    public string? FreeText10 { get; init; }
    public int? IsClubOrAssociationMember { get; init; }
    public int? DoNotReceiveNewsletters { get; init; }
    public int? LedgerId { get; init; }
    public bool? Mandate { get; init; }
    public string? MandateType { get; init; }
    public string? MandateId { get; init; }
    public DateOnly? MandateSignedDate { get; init; }
}

public sealed record InvoiceSummaryDto
{
    public required int Id { get; init; }
    public string? InvoiceNumber { get; init; }
    public int? RelationId { get; init; }
    public required DateOnly Date { get; init; }
    public string? Reference { get; init; }
    public string? Text { get; init; }
    public string? UrlPdfFile { get; init; }
    public decimal? TotalExcl { get; init; }
    public decimal? TotalAmount { get; init; }
    public decimal? VatAmount { get; init; }
    public int? TermOfPayment { get; init; }
    public int? TemplateId { get; init; }
    public bool? Print { get; init; }
}

public sealed record InvoiceDto
{
    public required int Id { get; init; }
    public string? InvoiceNumber { get; init; }
    public int? RelationId { get; init; }
    public required DateOnly Date { get; init; }
    public string? Reference { get; init; }
    public string? Text { get; init; }
    public int? TermOfPayment { get; init; }
    public string? InExVat { get; init; }
    public int? TemplateId { get; init; }
    public int? EmailTemplateId { get; init; }
    public decimal? TotalExcl { get; init; }
    public decimal? TotalAmount { get; init; }
    public decimal? VatAmount { get; init; }
    public string? UrlPdfFile { get; init; }
    public IReadOnlyList<InvoiceItemDto>? Items { get; init; }
}

public sealed record InvoiceItemDto
{
    public decimal? Quantity { get; init; }
    public decimal? Amount { get; init; }
    public int? UnitId { get; init; }
    public string? Code { get; init; }
    public int? ProductId { get; init; }
    public required string Description { get; init; }
    public decimal? PricePerUnit { get; init; }
    public required string VatCode { get; init; }
    public decimal? VatAmount { get; init; }
    public required int LedgerId { get; init; }
    public int? CostCenterId { get; init; }
    public decimal? DiscountAmount { get; init; }
    public decimal? DiscountPercentage { get; init; }
}

public sealed record EntrySummaryDto
{
    public required long Id { get; init; }
    public int? Type { get; init; }
    public required DateOnly Date { get; init; }
    public string? InvoiceNumber { get; init; }
    public int? LedgerId { get; init; }
    public decimal? Amount { get; init; }
    public string? EntryNumber { get; init; }
}

public sealed record EntryDto
{
    public required long Id { get; init; }
    public int? Type { get; init; }
    public required DateOnly Date { get; init; }
    public string? Description { get; init; }
    public int? TermOfPayment { get; init; }
    public int? LedgerId { get; init; }
    public int? RelationId { get; init; }
    public string? InExVat { get; init; }
    public string? InvoiceNumber { get; init; }
    public string? EntryNumber { get; init; }
    public IReadOnlyList<EntryRowDto>? Rows { get; init; }
    public IReadOnlyList<EntryVatDto>? Vat { get; init; }
}

public sealed record EntryRowDto
{
    public int? LedgerId { get; init; }
    public string? VatCode { get; init; }
    public decimal? VatAmount { get; init; }
    public int? CostCenterId { get; init; }
    public decimal? Amount { get; init; }
    public string? Description { get; init; }
    public string? InvoiceNumber { get; init; }
    public int? RelationId { get; init; }
}

public sealed record EntryVatDto
{
    public string? VatCode { get; init; }
    public decimal? Amount { get; init; }
}

public sealed record OutstandingInvoiceDto
{
    public required DateOnly Date { get; init; }
    public required long MutationId { get; init; }
    public string? InvoiceNumber { get; init; }
    public int? RelationId { get; init; }
    public string? RelationCode { get; init; }
    public string? Company { get; init; }
    public decimal? TotalAmount { get; init; }
    public decimal? PaidAmount { get; init; }
    public decimal? OutstandingAmount { get; init; }
}

public sealed record LedgerSummaryDto
{
    public required int Id { get; init; }
    public string? Code { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public string? Group { get; init; }
}

public sealed record LedgerDto
{
    public required int Id { get; init; }
    public string? Code { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public string? Group { get; init; }
}

public sealed record LedgerBalanceDto
{
    public required string Code { get; init; }
    public required string Type { get; init; }
    public required decimal Balance { get; init; }
}
