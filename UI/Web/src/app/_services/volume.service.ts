import {inject, Injectable} from '@angular/core';
import {environment} from "../../environments/environment";
import {HttpClient} from "@angular/common/http";
import {Volume} from "../_models/volume";
import {TextResonse} from "../_types/text-response";
import {UpdateVolume} from "../_models/update-volume";
import {
  ApplyRpgGeekCandidate,
  ApplyRpgGeekCandidatesBatch,
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

  updateVolume(volume: UpdateVolume) {
    return this.httpClient.post(this.baseUrl + 'volume/update', volume, TextResonse);
  }

  classifyRpgMaterialBatch(seriesId: number, items: RpgMaterialTypeUpdate[]) {
    return this.httpClient.post<number[]>(this.baseUrl + 'volume/rpg/classify-batch', {seriesId, items});
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
