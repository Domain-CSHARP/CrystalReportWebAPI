param (
    [string]$EmailSender,
    [string]$Recipients,
    [string]$CCRecipients,
    [string]$Subject,
    [string]$Body,
    [string]$Attach,
    [string]$BodyBase64
)

# =========================
# CONFIG
# =========================
$smtpServer = "192.168.2.14"
$smtpPort = 25

$logFolder = "C:\Temp\Emaillogs"
$logFile = "$logFolder\mail_error.log"

# =========================
# LOG FUNCTION
# =========================
function Write-Log {
    param([string]$message)

    try {
        # Ensure log folder exists
        if (!(Test-Path $logFolder)) {
            New-Item -ItemType Directory -Path $logFolder -Force | Out-Null
        }

        $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
        Add-Content -Path $logFile -Value "$timestamp $message"
    }
    catch {
        Write-Host "[LOG ERROR] $($_.Exception.Message)"
        Write-Host "[LOG MESSAGE] $message"
    }
}

# =========================
# START PROCESS
# =========================
try {

    Write-Log "========================================"
    Write-Log "Starting email process..."

    # =========================
    # DECODE BASE64 BODY
    # =========================
    if ($BodyBase64) {
        try {
            Write-Log "Decoding Base64 email body..."

            $decodedBytes = [System.Convert]::FromBase64String($BodyBase64)
            $Body = [System.Text.Encoding]::UTF8.GetString($decodedBytes)

            Write-Log "Base64 body decoded successfully."
        }
        catch {
            Write-Log "Failed to decode Base64 body. Error: $($_.Exception.Message)"
        }
    }

    # =========================
    # CREATE EMAIL
    # =========================
    Write-Log "Creating MailMessage object..."

    $emailMessage = New-Object System.Net.Mail.MailMessage

    $emailMessage.From = $EmailSender

    # =========================
    # TO RECIPIENTS
    # =========================
    Write-Log "Processing TO recipients..."

    $toEmails = $Recipients -split ";"

    foreach ($email in $toEmails) {

        $trimmedEmail = $email.Trim()

        if ($trimmedEmail) {
            $emailMessage.To.Add($trimmedEmail)
            Write-Log "Added TO recipient: $trimmedEmail"
        }
    }

    # =========================
    # CC RECIPIENTS
    # =========================
    if ($CCRecipients) {

        Write-Log "Processing CC recipients..."

        $ccEmails = $CCRecipients -split ";"

        foreach ($cc in $ccEmails) {

            $trimmedCC = $cc.Trim()

            if ($trimmedCC) {
                $emailMessage.CC.Add($trimmedCC)
                Write-Log "Added CC recipient: $trimmedCC"
            }
        }
    }

    # =========================
    # SUBJECT & BODY
    # =========================
    $emailMessage.Subject = $Subject

    if ($Body) {
        Write-Log "Converting newlines in email body to HTML line breaks (<br />)..."
        $emailMessage.Body = $Body -replace "`r`n", "<br />" -replace "`n", "<br />"
    } else {
        $emailMessage.Body = $Body
    }

    $emailMessage.IsBodyHtml = $true

    Write-Log "Email subject and body assigned."

    # =========================
    # ATTACHMENTS
    # =========================
    if ($Attach) {

        Write-Log "Processing attachments..."

        $attachments = $Attach -split ";"

        foreach ($file in $attachments) {

            $trimmedFile = $file.Trim()

            if ($trimmedFile) {

                if (Test-Path $trimmedFile) {

                    $emailMessage.Attachments.Add($trimmedFile)

                    Write-Log "Attachment added: $trimmedFile"
                }
                else {

                    Write-Log "Attachment file not found: $trimmedFile"
                }
            }
        }
    }

    # =========================
    # SMTP CLIENT
    # =========================
    Write-Log "Creating SMTP client..."

    $smtpClient = New-Object System.Net.Mail.SmtpClient($smtpServer, $smtpPort)

    $smtpClient.Timeout = 15000

    Write-Log "Sending email via $($smtpServer):$smtpPort to $Recipients"

    # =========================
    # SEND EMAIL
    # =========================
    $smtpClient.Send($emailMessage)

    Write-Log "Email sent successfully to $Recipients"

    Write-Host "Email sent successfully."

}
catch {

    $errorMessage = "Error sending email: $($_.Exception.Message)"

    if ($_.Exception.InnerException) {
        $errorMessage += " | Inner Error: $($_.Exception.InnerException.Message)"
    }

    Write-Log $errorMessage

    Write-Error $errorMessage

    exit 1
}
finally {

    Write-Log "Cleaning up resources..."

    if ($emailMessage) {

        foreach ($attachment in $emailMessage.Attachments) {
            $attachment.Dispose()
        }

        $emailMessage.Dispose()
    }

    Write-Log "Email process completed."
    Write-Log "========================================"
}