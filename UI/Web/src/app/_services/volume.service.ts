import {inject, Injectable} from '@angular/core';
import {environment} from "../../environments/environment";
import {HttpClient} from "@angular/common/http";
import {Volume} from "../_models/volume";
import {TextResonse} from "../_types/text-response";
import {UpdateVolume} from "../_models/update-volume";

@Injectable({
  providedIn: 'root'
})
export class VolumeService {
  private httpClient = inject(HttpClient);


  baseUrl = environment.apiUrl;

  getVolumeMetadata(volumeId: number) {
    return this.httpClient.get<Volume>(this.baseUrl + 'volume?volumeId=' + volumeId);
  }

  deleteVolume(volumeId: number) {
    return this.httpClient.delete<boolean>(this.baseUrl + 'volume?volumeId=' + volumeId);
  }

  deleteMultipleVolumes(volumeIds: number[]) {
    return this.httpClient.post<boolean>(this.baseUrl + "volume/multiple", volumeIds)
  }

  updateVolume(volume: UpdateVolume) {
    return this.httpClient.post(this.baseUrl + 'volume/update', volume, TextResonse);
  }

  refreshDriveThruRpgMetadata(volumeId: number) {
    return this.httpClient.post(this.baseUrl + `volume/drivethrurpg/refresh?volumeId=${volumeId}`, {});
  }

  getDriveThruRpgCandidates(volumeId: number, query: string | null = null) {
    const q = query && query.trim() ? `&query=${encodeURIComponent(query.trim())}` : '';
    return this.httpClient.get<Array<{productId: number, title: string}>>(this.baseUrl + `volume/drivethrurpg/candidates?volumeId=${volumeId}${q}`);
  }

  linkDriveThruRpg(volumeId: number, productId: number) {
    return this.httpClient.post(this.baseUrl + `volume/drivethrurpg/link?volumeId=${volumeId}&productId=${productId}`, {});
  }

  refreshRpgGeekMetadata(volumeId: number) {
    return this.httpClient.post(this.baseUrl + `volume/rpggeek/refresh?volumeId=${volumeId}`, {});
  }

  getRpgGeekCandidates(volumeId: number, query: string | null = null) {
    const q = query && query.trim() ? `&query=${encodeURIComponent(query.trim())}` : '';
    return this.httpClient.get<Array<{id: number, name: string, yearPublished?: number | null}>>(this.baseUrl + `volume/rpggeek/candidates?volumeId=${volumeId}${q}`);
  }

  linkRpgGeek(volumeId: number, rpgGeekId: number) {
    return this.httpClient.post(this.baseUrl + `volume/rpggeek/link?volumeId=${volumeId}&rpgGeekId=${rpgGeekId}`, {});
  }

}
