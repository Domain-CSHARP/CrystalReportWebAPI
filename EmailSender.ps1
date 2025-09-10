param (
    [string]$EmailSender,
    [string]$Recipients,
    [string]$CCRecipients,
    [string]$Subject,
    [string]$Body,
    [string]$Attach
)

# Define variables
$smtpServer = "www.domainjb.com" # Replace with your SMTP server
$smtpPort = 587 # Adjust if needed
$fromEmail = $EmailSender # Sender email
$toEmails = $Recipients -split ";" # Split recipients by semicolon
$ccEmails = $CCRecipients -split ";" # Split CC recipients by semicolon
$attachments = $Attach -split ";" # Split attachments by semicolon

$emailSubject = $Subject
$emailBody = $Body

# Ensure the Temp folder exists
if (!(Test-Path "C:\\Temp")) {
    New-Item -ItemType Directory -Path "C:\\Temp" | Out-Null
}

try {
    $emailMessage = New-Object System.Net.Mail.MailMessage
    $emailMessage.From = $fromEmail

    # Add To recipients
    foreach ($email in $toEmails) {
        if ($email -match "\S") { $emailMessage.To.Add($email.Trim()) }
    }

    # Add CC recipients
    foreach ($ccEmail in $ccEmails) {
        if ($ccEmail -match "\S") { $emailMessage.CC.Add($ccEmail.Trim()) }
    }

    $emailMessage.Subject = $emailSubject
    $emailMessage.Body = $emailBody

    # Add attachments
    foreach ($file in $attachments) {
        if ($file -match "\S" -and (Test-Path $file)) {
            $attachment = New-Object System.Net.Mail.Attachment($file)
            $emailMessage.Attachments.Add($attachment)
        }
    }

    # Configure SMTP client
    $smtpClient = New-Object System.Net.Mail.SmtpClient($smtpServer, $smtpPort)
    $smtpClient.Send($emailMessage)

    Write-Host "Email sent successfully to $($toEmails -join ', ') with CC to $($ccEmails -join ', ')."

} catch {
    Write-Host "Error sending email: $_"
} finally {
    # Dispose of the attachments and message to release the files
    foreach ($attachment in $emailMessage.Attachments) {
        $attachment.Dispose()
    }
    $emailMessage.Dispose()
}
