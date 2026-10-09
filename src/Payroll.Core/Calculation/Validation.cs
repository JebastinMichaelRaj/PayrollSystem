using System.ComponentModel.DataAnnotations;
using Payroll.Core.Domain;
namespace Payroll.Core.Calculation;

public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new();
    public bool Any => _errors.Count > 0;
    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list)) _errors[field] = list = new List<string>();
        list.Add(message);
    }
    public void ThrowIfAny()
    {
        if (Any) throw new ValidationFailedException(_errors.ToDictionary(k => k.Key, v => v.Value.ToArray()));
    }
}

public static class ModelValidator
{
    /// <summary>Runs DataAnnotations on the model and returns an error bag the caller can extend with cross-field rules.</summary>
    public static ValidationErrors Validate(object model)
    {
        var bag = new ValidationErrors();
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), results, validateAllProperties: true);
        foreach (var r in results)
            foreach (var member in r.MemberNames.DefaultIfEmpty(""))
                bag.Add(member, r.ErrorMessage ?? "Invalid value.");
        return bag;
    }
}
