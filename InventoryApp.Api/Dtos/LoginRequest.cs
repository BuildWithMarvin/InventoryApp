using System.ComponentModel.DataAnnotations;

namespace InventoryApp.Api.Dtos
{
    public record LoginRequest([Required] string BadgeBarcode);
}
