using System.Globalization;
using EBoekhouden.Api.Contracts;
using EBoekhouden.Mcp.Contracts;

namespace EBoekhouden.Mcp.Mapping;

public static class ResultMapper
{
    private static string? DecimalText(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    private static IReadOnlyList<TResult>? MapItems<TSource, TResult>(IReadOnlyList<TSource>? source,
        Func<TSource, TResult> map) where TSource : class
    {
        if (source is null)
            return null;
        if (source.Any(item => item is null))
            throw new InvalidDataException("Upstream nested collection contains a null element.");
        return source.Select(map).ToArray();
    }

    public static RelationSummaryResult Map(RelationSummaryDto source) => new()
    {
        Id = source.Id,
        Type = source.Type,
        Code = source.Code,
    };

    public static RelationResult Map(RelationDto source) => new()
    {
        Id = source.Id,
        Type = source.Type,
        Code = source.Code,
        Date = source.Date,
        Name = source.Name,
        Salutation = source.Salutation,
        Contact = source.Contact,
        Gender = source.Gender,
        Address = source.Address,
        PostalCode = source.PostalCode,
        City = source.City,
        Country = source.Country,
        Address2 = source.Address2,
        PostalCode2 = source.PostalCode2,
        City2 = source.City2,
        Country2 = source.Country2,
        PhoneNumber = source.PhoneNumber,
        MobilePhoneNumber = source.MobilePhoneNumber,
        FaxNumber = source.FaxNumber,
        EmailAddress = source.EmailAddress,
        EmailAddressInvoice = source.EmailAddressInvoice,
        EmailAddressReminder = source.EmailAddressReminder,
        Website = source.Website,
        Note = source.Note,
        VatNumber = source.VatNumber,
        Inactive = source.Inactive,
        TermOfPayment = source.TermOfPayment,
        CompanyRegistrationNumber = source.CompanyRegistrationNumber,
        Iban = source.Iban,
        Bic = source.Bic,
        FreeText1 = source.FreeText1,
        FreeText2 = source.FreeText2,
        FreeText3 = source.FreeText3,
        FreeText4 = source.FreeText4,
        FreeText5 = source.FreeText5,
        FreeText6 = source.FreeText6,
        FreeText7 = source.FreeText7,
        FreeText8 = source.FreeText8,
        FreeText9 = source.FreeText9,
        FreeText10 = source.FreeText10,
        IsClubOrAssociationMember = source.IsClubOrAssociationMember,
        DoNotReceiveNewsletters = source.DoNotReceiveNewsletters,
        LedgerId = source.LedgerId,
        Mandate = source.Mandate,
        MandateType = source.MandateType,
        MandateId = source.MandateId,
        MandateSignedDate = source.MandateSignedDate,
    };

    public static InvoiceSummaryResult Map(InvoiceSummaryDto source) => new()
    {
        Id = source.Id,
        InvoiceNumber = source.InvoiceNumber,
        RelationId = source.RelationId,
        Date = source.Date,
        Reference = source.Reference,
        Text = source.Text,
        UrlPdfFile = source.UrlPdfFile,
        TotalExcl = DecimalText(source.TotalExcl),
        TotalAmount = DecimalText(source.TotalAmount),
        VatAmount = DecimalText(source.VatAmount),
        TermOfPayment = source.TermOfPayment,
        TemplateId = source.TemplateId,
        Print = source.Print,
    };

    public static InvoiceResult Map(InvoiceDto source) => new()
    {
        Id = source.Id,
        InvoiceNumber = source.InvoiceNumber,
        RelationId = source.RelationId,
        Date = source.Date,
        Reference = source.Reference,
        Text = source.Text,
        TermOfPayment = source.TermOfPayment,
        InExVat = source.InExVat,
        TemplateId = source.TemplateId,
        EmailTemplateId = source.EmailTemplateId,
        TotalExcl = DecimalText(source.TotalExcl),
        TotalAmount = DecimalText(source.TotalAmount),
        VatAmount = DecimalText(source.VatAmount),
        UrlPdfFile = source.UrlPdfFile,
        Items = MapItems(source.Items, Map),
    };

    public static InvoiceItemResult Map(InvoiceItemDto source) => new()
    {
        Quantity = DecimalText(source.Quantity),
        Amount = DecimalText(source.Amount),
        UnitId = source.UnitId,
        Code = source.Code,
        ProductId = source.ProductId,
        Description = source.Description,
        PricePerUnit = DecimalText(source.PricePerUnit),
        VatCode = source.VatCode,
        VatAmount = DecimalText(source.VatAmount),
        LedgerId = source.LedgerId,
        CostCenterId = source.CostCenterId,
        DiscountAmount = DecimalText(source.DiscountAmount),
        DiscountPercentage = DecimalText(source.DiscountPercentage),
    };

    public static EntrySummaryResult Map(EntrySummaryDto source) => new()
    {
        Id = source.Id.ToString(CultureInfo.InvariantCulture),
        Type = source.Type,
        Date = source.Date,
        InvoiceNumber = source.InvoiceNumber,
        LedgerId = source.LedgerId,
        Amount = DecimalText(source.Amount),
        EntryNumber = source.EntryNumber,
    };

    public static EntryResult Map(EntryDto source) => new()
    {
        Id = source.Id.ToString(CultureInfo.InvariantCulture),
        Type = source.Type,
        Date = source.Date,
        Description = source.Description,
        TermOfPayment = source.TermOfPayment,
        LedgerId = source.LedgerId,
        RelationId = source.RelationId,
        InExVat = source.InExVat,
        InvoiceNumber = source.InvoiceNumber,
        EntryNumber = source.EntryNumber,
        Rows = MapItems(source.Rows, Map),
        Vat = MapItems(source.Vat, Map),
    };

    public static EntryRowResult Map(EntryRowDto source) => new()
    {
        LedgerId = source.LedgerId,
        VatCode = source.VatCode,
        VatAmount = DecimalText(source.VatAmount),
        CostCenterId = source.CostCenterId,
        Amount = DecimalText(source.Amount),
        Description = source.Description,
        InvoiceNumber = source.InvoiceNumber,
        RelationId = source.RelationId,
    };

    public static EntryVatResult Map(EntryVatDto source) => new()
    {
        VatCode = source.VatCode,
        Amount = DecimalText(source.Amount),
    };

    public static OutstandingInvoiceResult Map(OutstandingInvoiceDto source) => new()
    {
        Date = source.Date,
        MutationId = source.MutationId.ToString(CultureInfo.InvariantCulture),
        InvoiceNumber = source.InvoiceNumber,
        RelationId = source.RelationId,
        RelationCode = source.RelationCode,
        Company = source.Company,
        TotalAmount = DecimalText(source.TotalAmount),
        PaidAmount = DecimalText(source.PaidAmount),
        OutstandingAmount = DecimalText(source.OutstandingAmount),
    };

    public static LedgerSummaryResult Map(LedgerSummaryDto source) => new()
    {
        Id = source.Id,
        Code = source.Code,
        Description = source.Description,
        Category = source.Category,
        Group = source.Group,
    };

    public static LedgerResult Map(LedgerDto source) => new()
    {
        Id = source.Id,
        Code = source.Code,
        Description = source.Description,
        Category = source.Category,
        Group = source.Group,
    };

    public static LedgerBalanceResult Map(LedgerBalanceDto source) => new()
    {
        Code = source.Code,
        Type = source.Type,
        Balance = source.Balance.ToString(CultureInfo.InvariantCulture),
    };

}
