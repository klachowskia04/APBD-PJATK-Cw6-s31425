using ClinicApi.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace ClinicApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AppointmentsController : ControllerBase
{
    private readonly string _connectionString;

    public AppointmentsController(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection");
    }

    [HttpGet]
    public async Task<IActionResult> GetAppointments(
        string? status,
        string? patientLastName)
    {
        var result = new List<AppointmentListDto>();

        var connectionString = _connectionString;

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
        var connectionString = _connectionString;

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
    [HttpPost]
public async Task<IActionResult> CreateAppointment([FromBody] CreateAppointmentRequestDto request)
{
    
    if (request.AppointmentDate < DateTime.Now)
        return BadRequest("Data wizyty nie może być w przeszłości");

    if (string.IsNullOrWhiteSpace(request.Reason) || request.Reason.Length > 250)
        return BadRequest("Niepoprawny opis wizyty");

    await using var connection = new SqlConnection(_connectionString);
    await connection.OpenAsync();
    
    var checkPatientCmd = new SqlCommand("SELECT COUNT(1) FROM Patients WHERE IdPatient = @Id", connection);
    checkPatientCmd.Parameters.AddWithValue("@Id", request.IdPatient);

    var patientExists = (int)await checkPatientCmd.ExecuteScalarAsync();
    if (patientExists == 0)
        return BadRequest("Pacjent nie istnieje");
    
    var checkDoctorCmd = new SqlCommand("SELECT COUNT(1) FROM Doctors WHERE IdDoctor = @Id", connection);
    checkDoctorCmd.Parameters.AddWithValue("@Id", request.IdDoctor);

    var doctorExists = (int)await checkDoctorCmd.ExecuteScalarAsync();
    if (doctorExists == 0)
        return BadRequest("Lekarz nie istnieje");
    
    var conflictCmd = new SqlCommand(@"
        SELECT COUNT(1)
        FROM Appointments
        WHERE IdDoctor = @IdDoctor AND AppointmentDate = @Date
    ", connection);

    conflictCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
    conflictCmd.Parameters.AddWithValue("@Date", request.AppointmentDate);

    var conflict = (int)await conflictCmd.ExecuteScalarAsync();
    if (conflict > 0)
        return Conflict("Lekarz ma już wizytę w tym terminie");
    
    var insertCmd = new SqlCommand(@"
        INSERT INTO Appointments (IdPatient, IdDoctor, AppointmentDate, Status, Reason, CreatedAt)
        VALUES (@IdPatient, @IdDoctor, @Date, 'Scheduled', @Reason, GETDATE());
        SELECT SCOPE_IDENTITY();
    ", connection);

    insertCmd.Parameters.AddWithValue("@IdPatient", request.IdPatient);
    insertCmd.Parameters.AddWithValue("@IdDoctor", request.IdDoctor);
    insertCmd.Parameters.AddWithValue("@Date", request.AppointmentDate);
    insertCmd.Parameters.AddWithValue("@Reason", request.Reason);

    var newId = Convert.ToInt32(await insertCmd.ExecuteScalarAsync());

    return Created($"/api/appointments/{newId}", new { Id = newId });
    }
[HttpPut("{idAppointment}")]
public async Task<IActionResult> UpdateAppointment(int idAppointment, [FromBody] UpdateAppointmentRequestDto request)
{
    await using var connection = new SqlConnection(_connectionString);
    await connection.OpenAsync();

    var checkCmd = new SqlCommand("SELECT Status FROM Appointments WHERE IdAppointment = @Id", connection);
    checkCmd.Parameters.AddWithValue("@Id", idAppointment);

    var existingStatus = (string?)await checkCmd.ExecuteScalarAsync();

    if (existingStatus == null)
        return NotFound();

    if (existingStatus == "Completed" && request.AppointmentDate != default)
        return Conflict("Nie można zmienić terminu zakończonej wizyty");

    var allowedStatuses = new[] { "Scheduled", "Completed", "Cancelled" };
    if (!allowedStatuses.Contains(request.Status))
        return BadRequest("Niepoprawny status");

    var conflictCmd = new SqlCommand(@"
        SELECT COUNT(1)
        FROM Appointments
        WHERE IdDoctor = @DoctorId 
        AND AppointmentDate = @Date
        AND IdAppointment <> @Id
    ", connection);

    conflictCmd.Parameters.AddWithValue("@DoctorId", request.IdDoctor);
    conflictCmd.Parameters.AddWithValue("@Date", request.AppointmentDate);
    conflictCmd.Parameters.AddWithValue("@Id", idAppointment);

    var conflict = (int)await conflictCmd.ExecuteScalarAsync();
    if (conflict > 0)
        return Conflict("Lekarz ma już wizytę w tym terminie");

    var updateCmd = new SqlCommand(@"
        UPDATE Appointments
        SET IdPatient = @PatientId,
            IdDoctor = @DoctorId,
            AppointmentDate = @Date,
            Status = @Status,
            Reason = @Reason,
            InternalNotes = @Notes
        WHERE IdAppointment = @Id
    ", connection);

    updateCmd.Parameters.AddWithValue("@PatientId", request.IdPatient);
    updateCmd.Parameters.AddWithValue("@DoctorId", request.IdDoctor);
    updateCmd.Parameters.AddWithValue("@Date", request.AppointmentDate);
    updateCmd.Parameters.AddWithValue("@Status", request.Status);
    updateCmd.Parameters.AddWithValue("@Reason", request.Reason);
    updateCmd.Parameters.AddWithValue("@Notes", (object?)request.InternalNotes ?? DBNull.Value);
    updateCmd.Parameters.AddWithValue("@Id", idAppointment);

    await updateCmd.ExecuteNonQueryAsync();

    return Ok();
}
}