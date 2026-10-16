import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

export interface Device {
  id: string;
  deviceIdentifier: string;
  name: string;
  dashboardId?: string;
  dashboardName?: string;
  pairedAt: string;
  lastSeenAt?: string;
  firmwareVersion?: string;
  screenWidth?: number;
  screenHeight?: number;
}

export interface UpdateDeviceRequest {
  name?: string;
  dashboardId?: string | null;
}

export interface StartPairingResponse {
  code: string;
  expiresAt: string;
}

export interface PairingStatusResponse {
  status: string;
  deviceIdentifier?: string;
  expiresAt: string;
}

export interface PairingConfigurationResponse {
  clientUrl: string;
  serverAddress: string;
}

export interface ClaimDeviceResponse {
  id: string;
  deviceIdentifier: string;
  name: string;
  acknowledgementExpiresAt: string;
}

@Injectable({
  providedIn: 'root'
})
export class DeviceService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = '/api';

  getDevices(): Observable<Device[]> {
    return this.http.get<Device[]>(`${this.baseUrl}/devices`);
  }

  getDevicesForDashboard(dashboardId: string): Observable<Device[]> {
    return this.http.get<Device[]>(`${this.baseUrl}/devices/dashboard/${dashboardId}`);
  }

  updateDevice(deviceId: string, request: UpdateDeviceRequest): Observable<Device> {
    return this.http.put<Device>(`${this.baseUrl}/devices/${deviceId}`, request);
  }

  deleteDevice(deviceId: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/devices/${deviceId}`);
  }

  startPairing(): Observable<StartPairingResponse> {
    return this.http.post<StartPairingResponse>(`${this.baseUrl}/pairing/start`, {});
  }

  getPairingStatus(code: string): Observable<PairingStatusResponse> {
    return this.http.get<PairingStatusResponse>(`${this.baseUrl}/pairing/status`, { params: { code } });
  }

  getPairingConfiguration(): Observable<PairingConfigurationResponse> {
    return this.http.get<PairingConfigurationResponse>(`${this.baseUrl}/pairing/configuration`);
  }

  claimDevice(code: string): Observable<ClaimDeviceResponse> {
    return this.http.post<ClaimDeviceResponse>(`${this.baseUrl}/pairing/claim`, { code });
  }
}
