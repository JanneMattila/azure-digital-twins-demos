$eventHubNamespace = "<your event hub namespace name>"
$eventHubInstance = "<your event hub instance name>"
$tenantId = "<your tenant id>"

$url = "https://$eventHubNamespace.servicebus.windows.net/$eventHubInstance/messages?api-version=2014-01"
$url

Login-AzAccount
Login-AzAccount -Tenant $tenantId

$accessToken = Get-AzAccessToken -ResourceUrl https://eventhubs.azure.net
$secureAccessToken = ConvertTo-SecureString -AsPlainText -String $accessToken.Token

$body = ConvertTo-Json @{
    "id"    = "3752069677"
    "value" = 69
}

$body = ConvertTo-Json @{ 
    "_id"       = "Matiz"
    "carStatus" = "Stopped"
    "speed"     = 121.8
}

$body = ConvertTo-Json @{ 
    "_id"        = "LeftFront"
    "tyreStatus" = "OK"
    "pressure"   = 1.6
}

$body

###########################
# To send single message
Invoke-RestMethod `
    -Body $body `
    -ContentType "application/atom+xml;type=entry;charset=utf-8" `
    -Method "POST" `
    -Authentication Bearer `
    -Token $secureAccessToken `
    -Uri $url


###########################
# To send batch of messages
$body = ConvertTo-Json @(
    @{ 
        "Body" = ConvertTo-Json @{
            "_id"       = "Matiz"
            "carStatus" = "Stopping"
            "speed"     = 2.3
        }
    }
    @{ 
        "Body" = ConvertTo-Json @{
            "_id"        = "LeftFront"
            "tyreStatus" = "Flat"
            "pressure"   = 2.6
        }
    })

Invoke-RestMethod `
    -Body $body `
    -ContentType "application/vnd.microsoft.servicebus.json" `
    -Method "POST" `
    -Authentication Bearer `
    -Token $secureAccessToken `
    -Uri $url

# For SAS token based auth
# https://learn.microsoft.com/en-us/rest/api/eventhub/generate-sas-token
# https://learn.microsoft.com/en-us/rest/api/eventhub/event-hubs-runtime-rest
       
[Reflection.Assembly]::LoadWithPartialName("System.Web") | out-null
$accessPolicyName = "Send"
$accessPolicyKey = "<key from 'Send' policy with Send claims>"
# Token expiration 10 years
$expires = [string](([DateTimeOffset]::Now.ToUnixTimeSeconds()) + 24 * 60 * 60 * 365 * 10)
$signatureString = [System.Web.HttpUtility]::UrlEncode($url) + "`n$expires"
$hmac = New-Object System.Security.Cryptography.HMACSHA256
$hmac.key = [Text.Encoding]::ASCII.GetBytes($accessPolicyKey)
$signature = $hmac.ComputeHash([Text.Encoding]::ASCII.GetBytes($signatureString))
$signature = [Convert]::ToBase64String($Signature)
$sasToken = "sr=" + [System.Web.HttpUtility]::UrlEncode($url) + `
    "&sig=" + [System.Web.HttpUtility]::UrlEncode($Signature) + `
    "&se=" + $expires + `
    "&skn=" + $accessPolicyName
$sasToken

Invoke-RestMethod `
    -Body $body `
    -Headers @{"Authorization" = "SharedAccessSignature $sasToken" } `
    -ContentType "application/atom+xml;type=entry;charset=utf-8" `
    -Method "POST" `
    -Uri $url
