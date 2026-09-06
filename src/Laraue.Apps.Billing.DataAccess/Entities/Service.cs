using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Billing.DataAccess.Entities;

public class Service
{
    public ServiceId Id { get; set; }
    
    [MaxLength(32)]
    public required string Code { get; set; }
    
    [MaxLength(32)]
    public required string Name { get; set; }
}

public enum ServiceId
{
    LaraueBoards = 1,
    MarkdownTranslator = 2,
}