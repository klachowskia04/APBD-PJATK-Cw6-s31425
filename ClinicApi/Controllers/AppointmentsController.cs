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
    [HttpGet("{idAppointment}")]
    public async Task<IActionResult> GetAppointmentById(int idAppointment)
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();

        var sql = @"
SELECT 
    a.IdAppointment,
    a.AppointmentDate,
    a.Status,
    a.Reason,
    p.FirstName + ' ' + p.LastName,
    p.Email,
    p.PhoneNumber,
    d.FirstName + ' ' + d.LastName,
    d.LicenseNumber,
    a.InternalNotes,
    a.CreatedAt
FROM Appointments a
JOIN Patients p ON p.IdPatient = a.IdPatient
JOIN Doctors d ON d.IdDoctor = a.IdDoctor
WHERE a.IdAppointment = @IdAppointment";

        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@IdAppointment", idAppointment);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
            return NotFound();

        var result = new AppointmentDetailsDto
        {
            IdAppointment = reader.GetInt32(0),
            AppointmentDate = reader.GetDateTime(1),
            Status = reader.GetString(2),
            Reason = reader.GetString(3),
            PatientFullName = reader.GetString(4),
            PatientEmail = reader.GetString(5),
            PatientPhone = reader.IsDBNull(6) ? null : reader.GetString(6),
            DoctorFullName = reader.IsDBNull(7) ? null : reader.GetString(7),
            DoctorLicenseNumber = reader.IsDBNull(8) ? null : reader.GetString(8),
            InternalNotes = reader.IsDBNull(9) ? null : reader.GetString(9),
            CreatedAt = reader.GetDateTime(10)
        };

        return Ok(result);
    }
}