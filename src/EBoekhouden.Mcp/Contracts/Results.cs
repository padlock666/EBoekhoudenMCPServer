namespace EBoekhouden.Mcp.Contracts;

public sealed record RelationSummaryResult
{
    public required int Id { get; init; }
    public string? Type { get; init; }
    public string? Code { get; init; }
}

public sealed record RelationResult
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

public sealed record InvoiceSummaryResult
{
    public required int Id { get; init; }
    public string? InvoiceNumber { get; init; }
    public int? RelationId { get; init; }
    public required DateOnly Date { get; init; }
    public string? Reference { get; init; }
    public string? Text { get; init; }
    public string? UrlPdfFile { get; init; }
    public string? TotalExcl { get; init; }
    public string? TotalAmount { get; init; }
    public string? VatAmount { get; init; }
    public int? TermOfPayment { get; init; }
    public int? TemplateId { get; init; }
    public bool? Print { get; init; }
}

public sealed record InvoiceResult
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
    public string? TotalExcl { get; init; }
    public string? TotalAmount { get; init; }
    public string? VatAmount { get; init; }
    public string? UrlPdfFile { get; init; }
    public IReadOnlyList<InvoiceItemResult>? Items { get; init; }
}

public sealed record InvoiceItemResult
{
    public string? Quantity { get; init; }
    public string? Amount { get; init; }
    public int? UnitId { get; init; }
    public string? Code { get; init; }
    public int? ProductId { get; init; }
    public required string Description { get; init; }
    public string? PricePerUnit { get; init; }
    public required string VatCode { get; init; }
    public string? VatAmount { get; init; }
    public required int LedgerId { get; init; }
    public int? CostCenterId { get; init; }
    public string? DiscountAmount { get; init; }
    public string? DiscountPercentage { get; init; }
}

public sealed record EntrySummaryResult
{
    public required string Id { get; init; }
    public int? Type { get; init; }
    public required DateOnly Date { get; init; }
    public string? InvoiceNumber { get; init; }
    public int? LedgerId { get; init; }
    public string? Amount { get; init; }
    public string? EntryNumber { get; init; }
}

public sealed record EntryResult
{
    public required string Id { get; init; }
    public int? Type { get; init; }
    public required DateOnly Date { get; init; }
    public string? Description { get; init; }
    public int? TermOfPayment { get; init; }
    public int? LedgerId { get; init; }
    public int? RelationId { get; init; }
    public string? InExVat { get; init; }
    public string? InvoiceNumber { get; init; }
    public string? EntryNumber { get; init; }
    public IReadOnlyList<EntryRowResult>? Rows { get; init; }
    public IReadOnlyList<EntryVatResult>? Vat { get; init; }
}

public sealed record EntryRowResult
{
    public int? LedgerId { get; init; }
    public string? VatCode { get; init; }
    public string? VatAmount { get; init; }
    public int? CostCenterId { get; init; }
    public string? Amount { get; init; }
    public string? Description { get; init; }
    public string? InvoiceNumber { get; init; }
    public int? RelationId { get; init; }
}

public sealed record EntryVatResult
{
    public string? VatCode { get; init; }
    public string? Amount { get; init; }
}

public sealed record OutstandingInvoiceResult
{
    public required DateOnly Date { get; init; }
    public required string MutationId { get; init; }
    public string? InvoiceNumber { get; init; }
    public int? RelationId { get; init; }
    public string? RelationCode { get; init; }
    public string? Company { get; init; }
    public string? TotalAmount { get; init; }
    public string? PaidAmount { get; init; }
    public string? OutstandingAmount { get; init; }
}

public sealed record LedgerSummaryResult
{
    public required int Id { get; init; }
    public string? Code { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public string? Group { get; init; }
}

public sealed record LedgerResult
{
    public required int Id { get; init; }
    public string? Code { get; init; }
    public string? Description { get; init; }
    public string? Category { get; init; }
    public string? Group { get; init; }
}

public sealed record LedgerBalanceResult
{
    public required string Code { get; init; }
    public required string Type { get; init; }
    public required string Balance { get; init; }
}
