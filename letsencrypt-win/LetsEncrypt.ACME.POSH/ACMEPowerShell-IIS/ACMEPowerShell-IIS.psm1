﻿
#cd C:\prj\letsencrypt\solutions\letsencrypt-win\letsencrypt-win\LetsEncrypt.ACME.POSH
#Add-Type -Path .\bin\Debug\LetsEncrypt.ACME.POSH.dll

<#
Configure/install certs

Install-ACMECertificateToIIS -Ref <cert-ref>
	-ComputerName <target-server> - optional (defaults to local)
	-Website <website-name> - required
	-HostHeader <hostheader-name> - optional (defaults to none)
	-IPAddress <ip-address> - optional (defaults to all)
	-Port <port-num> - optional (defaults to 443)

#>

function Install-CertificateToIIS {
	param(
		[Parameter(Mandatory=$true)]
		[string]$Certificate,
		[Parameter(Mandatory=$true)]
		[string]$WebSite,
		[string]$IPAddress,
		[int]$Port,
		[string]$SNIHostname,
		[switch]$SNIRequired,
		[switch]$Replace,

		[System.Management.Automation.Runspaces.PSSession]$RemoteSession,

		## AWS POSH Base Params
		[object]$Region,
		[string]$AccessKey,
		[string]$SecretKey,
		[string]$SessionToken,
		[string]$ProfileName,
		[string]$ProfilesLocation,
		[Amazon.Runtime.AWSCredentials]$Credentials
	)

	## TODO:  We'll need to either "assume" that the user has
	## already imported the module or explicitly re-import it
	## and we'll also have to address the Default Noun Prefix
	##Import-Module ACMEPowerShell

	$ci = Get-ACMECertificate -Ref $Certificate
	if ($ci.IssuerSerialNumber) {
		$ic = Get-ACMEIssuerCertificate -SerialNumber $ci.IssuerSerialNumber
		if ($ic) {
			if (-not $ic.CrtPemFile -or -not (Test-Path -PathType Leaf $ic.CrtPemFile)) {
				throw "Unable to resolve Issuer Certificate PEM file"
			}
		}
	}

	if (-not $ci.KeyPemFile -or -not (Test-Path -PathType Leaf $ci.KeyPemFile)) {
		throw "Unable to resolve Private Key PEM file"
	}
	if (-not $ci.CrtPemFile -or -not (Test-Path -PathType Leaf $ci.CrtPemFile)) {
		throw "Unable to resolve Certificate PEM file"
	}

	## Export out the PFX to a local temp file
	$pfxTemp = [System.IO.Path]::GetTempFileName()
	$crt = Get-ACMECertificate -Ref $Certificate -ExportPkcs12 $pfxTemp -Overwrite
	if (-not $crt.Thumbprint) {
		throw "Unable to resolve certificate Thumbprint"
	}

	## Assemble a number of arguments and
	## settings based on input parameters
	$webBindingArgs = @{
		Name = $WebSite
		Protocol = "https"
	}
	$sslBinding = @{
		Host = "0.0.0.0"
		Port = "443"
	}

	if ($IPAddress) {
		$webBindingArgs.IPAddress = $IPAddress
		$sslBinding.Host = $IPAddress
	}
	if ($Port) {
		$webBindingArgs.Port = $Port
		$sslBinding.Port = "$Port"
	}
	if ($SNIHostname) {
		$webBindingArgs.HostHeader = $SNIHostname
	}
	
	## We craft a ScriptBlock to do the real work in such a way that we can invoke
	## it locally or remotely based on the right combination of input parameters
	[scriptblock]$script = {
		param(
			[string]$CrtThumbprint,
			[string]$pfxTemp,
			[byte[]]$pfxBytes,
			[bool]$SNIRequired,
			[bool]$Replace,
			[hashtable]$webBindingArgs,
			[hashtable]$sslBinding
		)

		Write-Warning "Params:"
		Write-Warning "  * $CrtThumbprint"
		Write-Warning "  * $pfxTemp"
		Write-Warning "  * $($pfxBytes.Length)"
		Write-Warning "  * $SNIRequired"
		Write-Warning "  * $Replace"
		Write-Warning "  * $webBindingArgs"
		Write-Warning "  * $sslBinding"

		## If we're running locally, then the PFX temp file already exists
		## If we're running remotely, we need to save the PFX bytes to a temp file
		if ($pfxBytes) {
			if (-not $pfxTemp) {
				$pfxTemp = [System.IO.Path]::GetTempFileName()
			}
			[System.IO.File]::WriteAllBytes($pfxTemp, $pfxBytes);
			Write-Verbose "Exported PFX bytes to temp file [$pfxTemp]"
		}

		## Import the PFX file to the local machine store and make sure its there
	    $crtPath = "Cert:\LocalMachine\My\$($CrtThumbprint)"
		if (Test-Path $crtPath -PathType Leaf) {
			Write-Warning "Existing certificate with matching Thumbprint found; SKIPPING"
		}
		else {
			Write-Verbose "Importing certificate from PFX [$pfxTemp]"
			Import-PfxCertificate -FilePath $pfxTemp -CertStoreLocation Cert:\LocalMachine\My -Exportable
			if (-not (Test-Path $crtPath -PathType Leaf)) {
	    		throw "Failed to import Certificate or import was misplaced"
			}
		}

		if (Test-Path $pfxTemp) {
			del $pfxTemp
		}

		## We need the MS Web Admin Module
		Import-Module WebAdministration

		## General guidelines for this procedure were borrowed from:
		##    http://www.iis.net/learn/manage/powershell/powershell-snap-in-configuring-ssl-with-the-iis-powershell-snap-in

		## See if there is already a matching Web Binding
		Write-Verbose "Testing for existing Web Binding"
		$existingWebBinding = Get-WebBinding @webBindingArgs
		if ($existingWebBinding) {
			Write-Warning "Existing Web Binding found matching specified parameters; SKIPPING"
		}
		else {
			$sslFlags = 0
			if ($SNIRequired) {
				$sslFlags = 1
			}

			Write-Verbose "Creating Web Binding..."
			New-WebBinding @webBindingArgs -SslFlags $sslFlags
			$newWebBinding = Get-WebBinding @webBindingArgs
			if (-not $newWebBinding) {
				throw "Failed to create new Web Binding"
			}
			Write-Verbose "Web Binding was created"
		}

		## See if there is already a matching SSL Binding
		Write-Verbose "Testing for existing SSL Binding"
		$sslBindingPath = "IIS:\SslBindings\$($sslBinding.Host):$($sslBinding.Port)"
		if (Test-Path -Path $sslBindingPath) {
			if ($Replace) {
				Write-Warning "Deleting existing SSL Binding";
				Remove-Item $sslBindingPath
			}
			else {
				throw "Existing SSL Binding found"
			}
		}
		Write-Verbose "Creating SSL Binding..."
		Get-Item $crtPath | New-Item $sslBindingPath
		$newSslBinding = Get-Item $sslBindingPath
		if (-not $newSslBinding) {
			throw "Failed to create new SSL Binding"
		}
		Write-Verbose "SSL Binding was created"
	}

	if ($RemoteSession)
	{
		$pfxBytes = [System.IO.File]::ReadAllBytes($pfxTemp);
		$invArgs = @(
			,$ci.Thumbprint
			,$null ## $pfxTemp
			,$pfxBytes
			,$SNIRequired.IsPresent
			,$Replace.IsPresent
			,$webBindingArgs
			,$sslBinding
		)
		Invoke-Command -Session $RemoteSession -ArgumentList $invArgs -ScriptBlock $script
	}
	else {
		$invArgs = @(
			,$ci.Thumbprint
			,$pfxTemp
			,$null ## $pfxBytes
			,$SNIRequired.IsPresent
			,$Replace.IsPresent
			,$webBindingArgs
			,$sslBinding
		)
		$script.Invoke($invArgs)
	}

	## Delete the local PFX temp file
	if (Test-Path $pfxTemp) {
		del $pfxTemp
	}
}

Export-ModuleMember -Function Install-CertificateToIIS
