using System.ComponentModel.DataAnnotations;

namespace PlannerWeb.Models;

public sealed class RegisterModel
{
    [Required(ErrorMessage = "Введите никнейм"), StringLength(32, MinimumLength = 3, ErrorMessage = "Никнейм — от 3 до 32 знаков"), RegularExpression(@"[\p{L}\p{N}_.-]+", ErrorMessage = "Никнейм: буквы, цифры, точка, дефис и подчёркивание")]
    public string Nickname { get; set; } = "";
    [Required(ErrorMessage = "Введите почту"), EmailAddress(ErrorMessage = "Введите корректную почту"), StringLength(254)]
    public string Email { get; set; } = "";
    [Required(ErrorMessage = "Введите пароль"), StringLength(128, MinimumLength = 10, ErrorMessage = "Пароль — от 10 до 128 знаков"), DataType(DataType.Password)]
    public string Password { get; set; } = "";
    [Compare(nameof(Password), ErrorMessage = "Пароли не совпадают"), DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = "";
}
public sealed class LoginModel
{
    [Required(ErrorMessage = "Введите почту или никнейм"), StringLength(254)] public string Login { get; set; } = "";
    [Required(ErrorMessage = "Введите пароль"), StringLength(128), DataType(DataType.Password)] public string Password { get; set; } = "";
    public string? ReturnUrl { get; set; }
}
