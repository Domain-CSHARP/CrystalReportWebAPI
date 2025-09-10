USE [NCHM]
GO

/****** Object:  StoredProcedure [dbo].[sp_ProcessInvoiceEmails]    Script Date: 10/09/2025 9:07:11 AM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

ALTER PROCEDURE [dbo].[sp_ProcessInvoiceEmails]
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @InvoiceNumber NVARCHAR(100)
    DECLARE @ErrorMessage NVARCHAR(4000)
    DECLARE @EmailKey INT
    DECLARE @LogMessage NVARCHAR(1000)

    -- Log start of process
    INSERT INTO EmailJobLog (ProcessName, Status, Message, CreatedDate)
    VALUES ('ProcessInvoiceEmails', 'Started', 'Beginning invoice email processing', GETDATE());

    BEGIN TRY 
        -- Check if there are pending emails to process
        IF NOT EXISTS (SELECT 1 FROM VALIDATED_LOCAL_INV_PENDING_EMAIL)
        BEGIN
            INSERT INTO EmailJobLog (ProcessName, Status, Message, CreatedDate)
            VALUES ('ProcessInvoiceEmails', 'Completed', 'No pending emails to process', GETDATE());
            RETURN;
        END

        -- Ensure old cursor is cleaned up before declaring
        IF CURSOR_STATUS('global','invoice_cursor') >= -1
        BEGIN
            CLOSE invoice_cursor;
            DEALLOCATE invoice_cursor;
        END

        -- Cursor to process each pending invoice
        DECLARE invoice_cursor CURSOR FOR
        SELECT invoice FROM VALIDATED_LOCAL_INV_PENDING_EMAIL;

        OPEN invoice_cursor;
        FETCH NEXT FROM invoice_cursor INTO @InvoiceNumber;

        WHILE @@FETCH_STATUS = 0
        BEGIN
            BEGIN TRANSACTION ProcessInvoice;

            -- Log processing of specific invoice
            SET @LogMessage = 'Processing invoice: ' + @InvoiceNumber;
            INSERT INTO EmailJobLog (ProcessName, Status, Message, CreatedDate, InvoiceNumber)
            VALUES ('ProcessInvoiceEmails', 'Processing', @LogMessage, GETDATE(), @InvoiceNumber);

            -- Check if invoice exists
            IF NOT EXISTS (SELECT 1 FROM INVOICE WHERE INVOICE = @InvoiceNumber)
            BEGIN
                SET @LogMessage = 'Invoice not found: ' + @InvoiceNumber;
                INSERT INTO EmailJobLog (ProcessName, Status, Message, CreatedDate, InvoiceNumber)
                VALUES ('ProcessInvoiceEmails', 'Error', @LogMessage, GETDATE(), @InvoiceNumber);

                ROLLBACK TRANSACTION ProcessInvoice;
                FETCH NEXT FROM invoice_cursor INTO @InvoiceNumber;
                CONTINUE;
            END

            -- Buyer information
            DECLARE @BuyerCode NVARCHAR(100)
            DECLARE @BuyerEmail NVARCHAR(500)
            DECLARE @BuyerEmailCC NVARCHAR(500)
            DECLARE @Field_BuyerEmail NVARCHAR(255)
            DECLARE @Field_BuyerEmailCC NVARCHAR(255)
            DECLARE @BuyerName NVARCHAR(255)
            DECLARE @InvoiceDate DATETIME
            DECLARE @ContractNumber NVARCHAR(100)

            SELECT
                @BuyerCode = accode,
                @InvoiceDate = date,
                @ContractNumber = smf
            FROM INVOICE
            WHERE INVOICE = @InvoiceNumber;

            SELECT TOP 1
                @Field_BuyerEmail = EMAIL_SERVER_FLD_RECIPIENT,
                @Field_BuyerEmailCC = EMAIL_SERVER_FLD_RECIPIENT_CC
            FROM PROFILE;

            SELECT
                @BuyerEmail = @Field_BuyerEmail, 
                @BuyerEmailCC = @Field_BuyerEmailCC,
                @BuyerName = NAME
            FROM ACCODE
            WHERE CODE = @BuyerCode;

            -- Validate buyer email
            IF ISNULL(@BuyerEmail, '') = ''
            BEGIN
                SET @LogMessage = 'Buyer email not found for invoice: ' + @InvoiceNumber + ', Buyer: ' + @BuyerName;
                INSERT INTO EmailJobLog (ProcessName, Status, Message, CreatedDate, InvoiceNumber)
                VALUES ('ProcessInvoiceEmails', 'Error', @LogMessage, GETDATE(), @InvoiceNumber);

                ROLLBACK TRANSACTION ProcessInvoice;
                FETCH NEXT FROM invoice_cursor INTO @InvoiceNumber;
                CONTINUE;
            END

            -- Subject
            DECLARE @Subject NVARCHAR(500);
            SET @Subject = 'E-INVOICE ' + @BuyerName + ' - DELIVERY DATED ' +
                          CONVERT(VARCHAR(2), DAY(@InvoiceDate)) + '/' +
                          CONVERT(VARCHAR(2), MONTH(@InvoiceDate)) + '/' +
                          RIGHT(CONVERT(VARCHAR(4), YEAR(@InvoiceDate)), 2);

            -- Body
            DECLARE @Body NVARCHAR(MAX);
            DECLARE @BodyTemplate NVARCHAR(MAX);

            SELECT @BodyTemplate = INVOICE_BODY_MSG FROM PROFILE;

            SET @Body = REPLACE(@BodyTemplate, '@INVOICE', @InvoiceNumber);
            SET @Body = REPLACE(@Body, '@INVDATE', CONVERT(VARCHAR(10), @InvoiceDate, 103));
            SET @Body = REPLACE(@Body, '@CONTACT', ISNULL((SELECT TOP 1 CONTACT1 FROM ACCODE WHERE CODE = @BuyerCode), ''));

            DECLARE @SenderDetails NVARCHAR(MAX);
            SELECT @SenderDetails = SENDER_DETAIL FROM acc_sign WHERE acc_sign = '';
            IF ISNULL(@SenderDetails, '') <> ''
            BEGIN
                SET @Body = @Body + @SenderDetails;
            END

            -- PDF path
            DECLARE @PdfPath NVARCHAR(500);
            DECLARE @PdfFolder NVARCHAR(4000);
            DECLARE @FileName NVARCHAR(4000);

            SELECT @PdfFolder = PDF FROM PROFILE;
            SET @FileName = @InvoiceNumber + '_' + CONVERT(VARCHAR(10), @InvoiceDate, 112) + '_' + @BuyerName + '.pdf';
            SET @PdfPath = @PdfFolder + '\INVPDF\' + CONVERT(VARCHAR(10), @InvoiceDate, 112) + '_' + @InvoiceNumber + '_' + @BuyerName + '\' + @FileName;

            -- CC recipients
            DECLARE @CCRecipients NVARCHAR(500);
            SELECT @CCRecipients = INVOICE_EMAIL FROM PROFILE;

            -- Truncate to fit
            SET @LogMessage   = LEFT(@LogMessage, 1000);
            SET @CCRecipients = LEFT(@CCRecipients, 150);
            SET @Subject      = LEFT(@Subject, 4000);
            SET @Body         = LEFT(@Body, 3000);
            SET @PdfPath      = LEFT(@PdfPath, 100);
            SET @InvoiceNumber = LEFT(@InvoiceNumber, 7);

            -- Insert mail log
            INSERT INTO LogMail_Inv (
                SENDER,
                CC,
                SUBJECT,
                BODY,
                ATTACH1,
                ATTACH2,
                ATTACH3,
                STATUS,
                DATE
            )
            VALUES (
                @BuyerEmail,
                @CCRecipients,
                @Subject,
                @Body,
                @PdfPath,
                NULL,
                NULL,
                0,
                GETDATE()
            );

            SET @EmailKey = SCOPE_IDENTITY();

            -- Update invoice
            UPDATE INVOICE
            SET EMAILSENT_USER = 'MIS',
                EMAILSENT_DATE = GETDATE()
            WHERE INVOICE = @InvoiceNumber;

            -- Success log
            SET @LogMessage = 'Successfully processed invoice: ' + @InvoiceNumber;
            INSERT INTO EmailJobLog (ProcessName, Status, Message, CreatedDate, InvoiceNumber)
            VALUES ('ProcessInvoiceEmails', 'Success', @LogMessage, GETDATE(), @InvoiceNumber);

            COMMIT TRANSACTION ProcessInvoice;

            FETCH NEXT FROM invoice_cursor INTO @InvoiceNumber;
        END

        CLOSE invoice_cursor;
        DEALLOCATE invoice_cursor;

        INSERT INTO EmailJobLog (ProcessName, Status, Message, CreatedDate)
        VALUES ('ProcessInvoiceEmails', 'Completed', 'Invoice email processing completed successfully', GETDATE());
    END TRY
    BEGIN CATCH
        SET @ErrorMessage = ERROR_MESSAGE();

        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        -- Cleanup cursor if exists
        IF CURSOR_STATUS('global','invoice_cursor') >= -1
        BEGIN
            CLOSE invoice_cursor;
            DEALLOCATE invoice_cursor;
        END

        INSERT INTO EmailJobLog (ProcessName, Status, Message, CreatedDate)
        VALUES ('ProcessInvoiceEmails', 'Failed', @ErrorMessage, GETDATE());

        -- Optional: let job detect failure
        -- THROW;
    END CATCH
END
GO


