using ClinicApi.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace ClinicApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AppointmentsController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet]
    public async Task<IActionResult> GetAppointments(
        string? status,
        string? patientLastName)
    {
        var result = new List<AppointmentListDto>();

        var connectionString =
            _configuration.GetConnectionString("DefaultConnection");

        await using var connection =
            new SqlConnection(connectionString);

        await connection.OpenAsync();

        var sql = @"
SELECT
    a.IdAppointment,
    a.AppointmentDate,
    a.Status,
    a.Reason,
    p.FirstName + ' ' + p.LastName AS PatientFullName,
    p.Email
FROM Appointments a
JOIN Patients p ON p.IdPatient = a.IdPatient
WHERE (@Status IS NULL OR a.Status = @Status)
AND (@PatientLastName IS NULL OR p.LastName = @PatientLastName)
ORDER BY a.AppointmentDate";

        await using var command =
            new SqlCommand(sql, connection);

        command.Parameters.AddWithValue("@Status",
            (object?)status ?? DBNull.Value);

        command.Parameters.AddWithValue("@PatientLastName",
            (object?)patientLastName ?? DBNull.Value);

        await using var reader =
            await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            result.Add(new AppointmentListDto
            {
                IdAppointment = reader.GetInt32(0),
                AppointmentDate = reader.GetDateTime(1),
                Status = reader.GetString(2),
                Reason = reader.GetString(3),
                PatientFullName = reader.GetString(4),
                PatientEmail = reader.GetString(5)
            });
        }

        return Ok(result);
    }
}