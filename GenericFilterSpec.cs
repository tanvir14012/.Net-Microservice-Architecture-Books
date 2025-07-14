using Ardalis.Specification;
using LINQKit;
using Microsoft.EntityFrameworkCore;
using System.Linq.Dynamic.Core;

public class GenericFilterSpec<T> : Specification<T>
{
    public GenericFilterSpec(
        List<FilterCriteria> filters,
        List<SortCriteria> sortColumns,
        int pageNumber,
        int pageSize)
    {
        if (filters != null && filters.Any())
        {
            // LINQKit: Start with a dynamic predicate
            var predicate = PredicateBuilder.New<T>(true);

            foreach (var filter in filters)
            {
                var column = filter.Column;
                var op = filter.Operator;
                var value = filter.Value;
                var isOr = filter.IsOr;

                // Build individual expression for this filter
                var individualPredicate = BuildPredicate<T>(column, op, value);

                if (isOr)
                    predicate = predicate.Or(individualPredicate);
                else
                    predicate = predicate.And(individualPredicate);
            }

            Query.Where(predicate);
        }

        if (sortColumns != null && sortColumns.Any())
        {
            var orderingString = string.Join(", ", 
                sortColumns.Select(sc => $"{sc.Column} {(sc.Descending ? "DESC" : "ASC")}"));

            Query.OrderBy(orderingString);
        }

        Query.Skip((pageNumber - 1) * pageSize).Take(pageSize);
    }

    private static Expression<Func<T, bool>> BuildPredicate<T>(string column, string op, object value)
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var member = Expression.PropertyOrField(parameter, column);

        Expression constant = Expression.Constant(value);

        // Convert value to proper type
        if (value != null && member.Type != value.GetType())
        {
            constant = Expression.Convert(Expression.Constant(Convert.ChangeType(value, member.Type)), member.Type);
        }

        Expression body;

        switch (op.ToLower())
        {
            case "equals":
            case "==":
                body = Expression.Equal(member, constant);
                break;

            case "notequals":
            case "!=":
                body = Expression.NotEqual(member, constant);
                break;

            case "contains":
                body = Expression.Call(member, nameof(string.Contains), null, constant);
                break;

            case "startswith":
                body = Expression.Call(member, nameof(string.StartsWith), null, constant);
                break;

            case "endswith":
                body = Expression.Call(member, nameof(string.EndsWith), null, constant);
                break;

            case ">":
                body = Expression.GreaterThan(member, constant);
                break;

            case ">=":
                body = Expression.GreaterThanOrEqual(member, constant);
                break;

            case "<":
                body = Expression.LessThan(member, constant);
                break;

            case "<=":
                body = Expression.LessThanOrEqual(member, constant);
                break;

            case "like":
                body = Expression.Call(
                    typeof(DbFunctionsExtensions),
                    nameof(DbFunctionsExtensions.Like),
                    Type.EmptyTypes,
                    Expression.Property(null, typeof(EF), nameof(EF.Functions)),
                    member,
                    constant);
                break;

            default:
                throw new NotSupportedException($"Operator '{op}' is not supported.");
        }

        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }
}

public class FilterCriteria
{
    public string Column { get; set; }
    public string Operator { get; set; } // Equals, Contains, >, <, etc.
    public object Value { get; set; }
    public bool IsOr { get; set; } = false; // false = AND, true = OR
}

public class SortCriteria
{
    public string Column { get; set; }
    public bool Descending { get; set; }
}


//Usage
var filters = new List<FilterCriteria>
{
    new FilterCriteria { Column = "Name", Operator = "Contains", Value = "John", IsOr = false },
    new FilterCriteria { Column = "Age", Operator = ">", Value = 30, IsOr = false },
    new FilterCriteria { Column = "City", Operator = "Equals", Value = "New York", IsOr = true }
};

var sortColumns = new List<SortCriteria>
{
    new SortCriteria { Column = "LastName", Descending = true },
    new SortCriteria { Column = "FirstName", Descending = false }
};

var spec = new GenericFilterSpec<Person>(filters, sortColumns, pageNumber: 1, pageSize: 20);
