using System.Data;
using Microsoft.AspNetCore.Mvc;
using WebApplication1.model;
using System.Data.SqlClient;


namespace WebApplication1.controller;

public class WarehouseController
{
    [ApiController]
    [Route("[controller]")]
    public class WarehouseController : ControllerBase
    {
        private readonly string connectionString = configuration.GetConnectionString("SqliteConnection");

        [HttpPost]
        public IActionResult AddProductToWarehouse([FromBody] ProductWarehouseDto productWarehouse)
        {
            if (productWarehouse.Amount <= 0)
            {
                return BadRequest("Quantity must be greater than zero.");
            }

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                if (!ProductExists(connection, productWarehouse.IdProduct))
                {
                    return NotFound("Product does not exist.");
                }

                if (!WarehouseExists(connection, productWarehouse.IdWarehouse))
                {
                    return NotFound("Warehouse does not exist.");
                }

                var orderDetails = GetOrderDetails(connection, productWarehouse.IdProduct, productWarehouse.Amount, productWarehouse.CreatedAt);
                if (orderDetails == null)
                {
                    return NotFound("No valid order found that can be fulfilled.");
                }

                if (IsOrderFulfilled(connection, orderDetails.Item1))
                {
                    return BadRequest("Order is already fulfilled.");
                }

                var productWarehouseId = InsertProductWarehouse(connection, productWarehouse, orderDetails.Item1, orderDetails.Item2);
                if (productWarehouseId == -1)
                {
                    return StatusCode(500, "Failed to insert the product into the warehouse.");
                }

                UpdateOrderFulfilledAt(connection, orderDetails.Item1, productWarehouse.CreatedAt);

                return Ok(new { ProductWarehouseId = productWarehouseId });
            }
        }

        [HttpPost("AddWithStoredProc")]
        public IActionResult AddProductToWarehouseWithStoredProc([FromBody] ProductWarehouseDto productWarehouse)
        {
            if (productWarehouse.Amount <= 0)
            {
                return BadRequest("Quantity must be greater than zero.");
            }

            using (var connection = new SqlConnection(connectionString))
            {
                try
                {
                    connection.Open();
                    using (var command = new SqlCommand("AddProductToWarehouse", connection))
                    {
                        command.CommandType = CommandType.StoredProcedure;
                        command.Parameters.AddWithValue("@IdProduct", productWarehouse.IdProduct);
                        command.Parameters.AddWithValue("@IdWarehouse", productWarehouse.IdWarehouse);
                        command.Parameters.AddWithValue("@Amount", productWarehouse.Amount);
                        command.Parameters.AddWithValue("@CreatedAt", productWarehouse.CreatedAt);

                        var result = command.ExecuteNonQuery();
                        if (result == 0)
                        {
                            return NotFound("No rows affected; possibly no such order or warehouse/product not found.");
                        }
                        
                        return Ok("Product added to warehouse successfully.");
                    }
                }
                catch (SqlException ex)
                {
                    return StatusCode(500, "Internal server error: " + ex.Message);
                }
            }
        }

        
        private bool ProductExists(SqlConnection connection, int productId)
        {
            using (var command = new SqlCommand("SELECT COUNT(*) FROM Product WHERE IdProduct = @IdProduct", connection))
            {
                command.Parameters.AddWithValue("@IdProduct", productId);
                return (int)command.ExecuteScalar() > 0;
            }
        }

        private bool WarehouseExists(SqlConnection connection, int warehouseId)
        {
            using (var command = new SqlCommand("SELECT COUNT(*) FROM Warehouse WHERE IdWarehouse = @IdWarehouse", connection))
            {
                command.Parameters.AddWithValue("@IdWarehouse", warehouseId);
                return (int)command.ExecuteScalar() > 0;
            }
        }

        private Tuple<int, decimal> GetOrderDetails(SqlConnection connection, int productId, int amount, DateTime createdAt)
        {
            using (var command = new SqlCommand("SELECT TOP 1 o.IdOrder, p.Price FROM [Order] o JOIN Product p ON o.IdProduct = p.IdProduct WHERE o.IdProduct = @IdProduct AND o.Amount >= @Amount AND o.CreatedAt < @CreatedAt AND NOT EXISTS (SELECT 1 FROM Product_Warehouse WHERE IdOrder = o.IdOrder)", connection))
            {
                command.Parameters.AddWithValue("@IdProduct", productId);
                command.Parameters.AddWithValue("@Amount", amount);
                command.Parameters.AddWithValue("@CreatedAt", createdAt);
                using (var reader = command.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return Tuple.Create(reader.GetInt32(0), reader.GetDecimal(1));
                    }
                    return null;
                }
            }
        }

        private bool IsOrderFulfilled(SqlConnection connection, int orderId)
        {
            using (var command = new SqlCommand("SELECT COUNT(*) FROM Product_Warehouse WHERE IdOrder = @IdOrder", connection))
            {
                command.Parameters.AddWithValue("@IdOrder", orderId);
                return (int)command.ExecuteScalar() > 0;
            }
        }

        private int InsertProductWarehouse(SqlConnection connection, ProductWarehouseDto productWarehouse, int orderId, decimal price)
        {
            using (var command = new SqlCommand("INSERT INTO Product_Warehouse (IdWarehouse, IdProduct, IdOrder, Amount, Price, CreatedAt) VALUES (@IdWarehouse, @IdProduct, @IdOrder, @Amount, @Amount*@Price, @CreatedAt); SELECT SCOPE_IDENTITY();", connection))
            {
                command.Parameters.AddWithValue("@IdWarehouse", productWarehouse.IdWarehouse);
                command.Parameters.AddWithValue("@IdProduct", productWarehouse.IdProduct);
                command.Parameters.AddWithValue("@IdOrder", orderId);
                command.Parameters.AddWithValue("@Amount", productWarehouse.Amount);
                command.Parameters.AddWithValue("@Price", price * productWarehouse.Amount);
                command.Parameters.AddWithValue("@CreatedAt", productWarehouse.CreatedAt);
                var result = command.ExecuteScalar();
                return result != null ? Convert.ToInt32(result) : -1;
            }
        }

        private void UpdateOrderFulfilledAt(SqlConnection connection, int orderId, DateTime fulfilledAt)
        {
            using (var command = new SqlCommand("UPDATE [Order] SET FulfilledAt = @FulfilledAt WHERE IdOrder = @IdOrder", connection))
            {
                command.Parameters.AddWithValue("@IdOrder", orderId);
                command.Parameters.AddWithValue("@FulfilledAt", fulfilledAt);
                command.ExecuteNonQuery();
            }
        }
    }
}