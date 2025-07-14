using Ardalis.Specification;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq.Expressions;
using PrivilegePro.Models.ViewModels;

namespace PrivilegePro.Infrastructure.Specs
{
    public class PaginatedFilterSpec<T> : Specification<T> where T : class
    {
        public PaginatedFilterSpec(PaginatedFilterParameter<T> parameters)
        {
            ApplyFiltering(parameters);
            ApplySorting(parameters);
            ApplyPaging(parameters);
        }

        private void ApplyFiltering(PaginatedFilterParameter<T> parameters)
        {
            if (!string.IsNullOrWhiteSpace(parameters.SearchTerm) && parameters.SearchableColumns.Any())
            {
                Expression<Func<T, bool>> combinedPredicate = null;

                foreach (var column in parameters.SearchableColumns)
                {
                    var parameter = Expression.Parameter(typeof(T), "e");
                    var property = Expression.PropertyOrField(parameter, column);

                    if (property.Type != typeof(string))
                        continue;

                    var likeCall = Expression.Call(
                        typeof(DbFunctionsExtensions),
                        nameof(DbFunctionsExtensions.Like),
                        Type.EmptyTypes,
                        Expression.Constant(EF.Functions),
                        property,
                        Expression.Constant($"%{parameters.SearchTerm}%"));

                    var lambda = Expression.Lambda<Func<T, bool>>(likeCall, parameter);

                    combinedPredicate = combinedPredicate == null
                        ? lambda
                        : CombineWithOr(combinedPredicate, lambda);
                }

                if (combinedPredicate != null)
                    Query.Where(combinedPredicate);
            }

            if (parameters.Filters?.Any() == true)
            {
                foreach (var filter in parameters.Filters)
                {
                    var parameter = Expression.Parameter(typeof(T), "e");
                    var property = Expression.PropertyOrField(parameter, filter.Key);

                    var constant = Expression.Constant(Convert.ChangeType(filter.Value, property.Type));
                    var equal = Expression.Equal(property, constant);

                    var lambda = Expression.Lambda<Func<T, bool>>(equal, parameter);
                    Query.Where(lambda);
                }
            }
        }

        private void ApplySorting(PaginatedFilterParameter<T> parameters)
        {
            IOrderedSpecificationBuilder<T> orderedQuery = null;

            for (int i = 0; i < parameters.SortableColumns.Count; i++)
            {
                var sort = parameters.SortableColumns[i];

                var parameter = Expression.Parameter(typeof(T), "e");
                var property = Expression.PropertyOrField(parameter, sort.Column);
                var convert = Expression.Convert(property, typeof(object));
                var lambda = Expression.Lambda<Func<T, object>>(convert, parameter);

                if (sort.Descending)
                {
                    orderedQuery = i == 0
                        ? Query.OrderByDescending(lambda)
                        : orderedQuery.ThenByDescending(lambda);
                }
                else
                {
                    orderedQuery = i == 0
                        ? Query.OrderBy(lambda)
                        : orderedQuery.ThenBy(lambda);
                }
            }
        }

        private void ApplyPaging(PaginatedFilterParameter<T> parameters)
        {
            var skip = (parameters.PageNumber - 1) * parameters.PageSize;
            Query.Skip(skip).Take(parameters.PageSize);
        }

        private Expression<Func<T, bool>> CombineWithOr(
            Expression<Func<T, bool>> left,
            Expression<Func<T, bool>> right)
        {
            var parameter = Expression.Parameter(typeof(T));
            var combined = Expression.OrElse(
                Expression.Invoke(left, parameter),
                Expression.Invoke(right, parameter));
            return Expression.Lambda<Func<T, bool>>(combined, parameter);
        }
    }
}


//USAGE

var parameters = new PaginatedFilterParameter<Agent>
{
    SearchTerm = "john",
    SearchableColumns = new[] { "Name", "Email" },
    SortableColumns = new List<SortOption>
    {
        new SortOption { Column = "CreatedAt", Descending = true },
        new SortOption { Column = "Name", Descending = false }
    },
    PageNumber = 1,
    PageSize = 25,
    Filters = new Dictionary<string, object>
    {
        { "IsActive", true }
    }
};

var spec = new PaginatedFilterSpec<Agent>(parameters);
var agents = await repository.ListAsync(spec);


//Result

namespace PrivilegePro.Models.ViewModels
{
    public class PagedResult<T>
    {
        /// <summary>
        /// Total number of records in the database (before filters)
        /// </summary>
        public int Total { get; set; }

        /// <summary>
        /// Total number of records after applying filters/search
        /// </summary>
        public int TotalFiltered { get; set; }

        /// <summary>
        /// Current page number (1-based)
        /// </summary>
        public int PageNumber { get; set; }

        /// <summary>
        /// Number of records per page
        /// </summary>
        public int PageSize { get; set; }

        /// <summary>
        /// The paginated and filtered data
        /// </summary>
        public List<T> Data { get; set; } = new();
    }
}


//All in one go
public static async Task<PagedResult<TResult>> ToPagedResultAsync<TEntity, TResult>(
    this IRepository<TEntity> repository,
    PaginatedFilterParameter<TEntity> parameters,
    IMapper mapper)
    where TEntity : class
{
    var query = repository.AsQueryable();

    // Total count before filtering
    var totalCount = await query.CountAsync();

    // Apply filters
    if (!string.IsNullOrWhiteSpace(parameters.SearchTerm) && parameters.SearchableColumns.Any())
    {
        var searchPredicate = BuildSearchPredicate<TEntity>(parameters.SearchTerm, parameters.SearchableColumns);
        query = query.Where(searchPredicate);
    }

    if (parameters.Filters?.Any() == true)
    {
        foreach (var filter in parameters.Filters)
        {
            query = query.Where(BuildEqualsPredicate<TEntity>(filter.Key, filter.Value));
        }
    }

    // Total count after filtering
    var filteredCount = await query.CountAsync();

    // Apply sorting
    if (parameters.SortableColumns.Any())
    {
        IOrderedQueryable<TEntity> orderedQuery = null;
        for (int i = 0; i < parameters.SortableColumns.Count; i++)
        {
            var sort = parameters.SortableColumns[i];
            orderedQuery = i == 0
                ? ApplyOrder(query, sort.Column, sort.Descending)
                : ApplyThenOrder(orderedQuery, sort.Column, sort.Descending);
        }
        query = orderedQuery;
    }

    // Apply paging
    var skip = (parameters.PageNumber - 1) * parameters.PageSize;
    query = query.Skip(skip).Take(parameters.PageSize);

    // Fetch paged data
    var dataEntities = await query.ToListAsync();
    var dataDtos = mapper.Map<List<TResult>>(dataEntities);

    return new PagedResult<TResult>
    {
        Total = totalCount,
        TotalFiltered = filteredCount,
        PageNumber = parameters.PageNumber,
        PageSize = parameters.PageSize,
        Data = dataDtos
    };
}

private static Expression<Func<TEntity, bool>> BuildSearchPredicate<TEntity>(string searchTerm, IEnumerable<string> columns)
{
    var parameter = Expression.Parameter(typeof(TEntity), "e");
    Expression body = null;

    foreach (var column in columns)
    {
        var property = Expression.PropertyOrField(parameter, column);
        if (property.Type != typeof(string))
            continue;

        var likeCall = Expression.Call(
            typeof(DbFunctionsExtensions),
            nameof(DbFunctionsExtensions.Like),
            Type.EmptyTypes,
            Expression.Constant(EF.Functions),
            property,
            Expression.Constant($"%{searchTerm}%"));

        body = body == null ? likeCall : Expression.OrElse(body, likeCall);
    }

    return Expression.Lambda<Func<TEntity, bool>>(body, parameter);
}

private static Expression<Func<TEntity, bool>> BuildEqualsPredicate<TEntity>(string propertyName, object value)
{
    var parameter = Expression.Parameter(typeof(TEntity), "e");
    var property = Expression.PropertyOrField(parameter, propertyName);
    var constant = Expression.Constant(Convert.ChangeType(value, property.Type));
    var equal = Expression.Equal(property, constant);

    return Expression.Lambda<Func<TEntity, bool>>(equal, parameter);
}

private static IOrderedQueryable<TEntity> ApplyOrder<TEntity>(
    IQueryable<TEntity> query, string propertyName, bool descending)
{
    return descending
        ? query.OrderByDescending(BuildPropertyLambda<TEntity>(propertyName))
        : query.OrderBy(BuildPropertyLambda<TEntity>(propertyName));
}

private static IOrderedQueryable<TEntity> ApplyThenOrder<TEntity>(
    IOrderedQueryable<TEntity> query, string propertyName, bool descending)
{
    return descending
        ? query.ThenByDescending(BuildPropertyLambda<TEntity>(propertyName))
        : query.ThenBy(BuildPropertyLambda<TEntity>(propertyName));
}

private static Expression<Func<TEntity, object>> BuildPropertyLambda<TEntity>(string propertyName)
{
    var parameter = Expression.Parameter(typeof(TEntity), "e");
    var property = Expression.PropertyOrField(parameter, propertyName);
    var converted = Expression.Convert(property, typeof(object));
    return Expression.Lambda<Func<TEntity, object>>(converted, parameter);
}

var pagedResult = await repository.ToPagedResultAsync<Agent, AgentDto>(parameters, mapper);




