import {inject, Injectable} from '@angular/core';
import {environment} from "../../environments/environment";
import {HttpClient} from "@angular/common/http";
import {Volume} from "../_models/volume";
import {UpdateVolumeRequest} from "../_models/update-volume-request";
import {
  ApplyRpgGeekCandidate,
  ApplyRpgGeekCandidatesBatch,
  DriveThruRpgCandidateSearchResult,
  RpgGeekCandidatePreview,
  RpgGeekCandidateSearchResult,
  RpgMaterialTypeUpdate
} from "../_models/rpg/rpg-catalog";

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

  updateVolume(volume: UpdateVolumeRequest) {
    return this.httpClient.post<Volume>(this.baseUrl + 'volume/update', volume);
  }

  classifyRpgMaterialBatch(seriesId: number, items: RpgMaterialTypeUpdate[]) {
    return this.httpClient.post<number[]>(this.baseUrl + 'volume/rpg/classify-batch', {seriesId, items});
  }

  searchDriveThruRpgCandidates(volumeId: number, query: string) {
    const params = new URLSearchParams({volumeId: String(volumeId)});
    if (query.trim()) params.set('query', query.trim());
    return this.httpClient.get<DriveThruRpgCandidateSearchResult>(
      this.baseUrl + 'volume/rpg/drivethrurpg/candidates?' + params.toString());
  }

  linkDriveThruRpg(volumeId: number, productId: number) {
    const params = new URLSearchParams({volumeId: String(volumeId), productId: String(productId)});
    return this.httpClient.post<boolean>(this.baseUrl + 'volume/rpg/drivethrurpg/link?' + params.toString(), null);
  }

  refreshDriveThruRpg(volumeId: number) {
    const params = new URLSearchParams({volumeId: String(volumeId)});
    return this.httpClient.post(this.baseUrl + 'volume/rpg/drivethrurpg/refresh?' + params.toString(), null);
  }

  searchRpgGeekCandidates(volumeId: number, query: string, forceRefresh = false) {
    const params = new URLSearchParams({volumeId: String(volumeId), query});
    if (forceRefresh) params.set('forceRefresh', 'true');
    return this.httpClient.get<RpgGeekCandidateSearchResult>(
      this.baseUrl + 'volume/rpg/geek/candidates?' + params.toString());
  }

  previewRpgGeekCandidate(volumeId: number, productId: number, forceRefresh = false) {
    const params = new URLSearchParams({volumeId: String(volumeId), productId: String(productId)});
    if (forceRefresh) params.set('forceRefresh', 'true');
    return this.httpClient.get<RpgGeekCandidatePreview>(
      this.baseUrl + 'volume/rpg/geek/preview?' + params.toString());
  }

  applyRpgGeekCandidate(request: ApplyRpgGeekCandidate) {
    return this.httpClient.post<boolean>(this.baseUrl + 'volume/rpg/geek/apply', request);
  }

  applyRpgGeekCandidatesBatch(request: ApplyRpgGeekCandidatesBatch) {
    return this.httpClient.post<boolean>(this.baseUrl + 'volume/rpg/geek/apply-batch', request);
  }

}
