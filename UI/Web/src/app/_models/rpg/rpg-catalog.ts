import type {Volume} from "../volume";

export enum RpgMaterialType {
  Unclassified = 0,
  CoreManual = 1,
  Manual = 2,
  Adventure = 3,
  Supplement = 4,
  OtherPublication = 5,
  Map = 6,
  CharacterSheet = 7,
  GameAid = 8,
  CardsAndTokens = 9,
  OtherResource = 10
}

export enum RpgGeekMatchStatus {
  NotSearched = 0,
  Pending = 1,
  Candidate = 2,
  NoMatch = 3,
  Ambiguous = 4,
  Failed = 5,
  Linked = 6
}

export enum DriveThruRpgMatchStatus {
  NotSearched = 0,
  Pending = 1,
  Candidate = 2,
  NoMatch = 3,
  Ambiguous = 4,
  Failed = 5,
  Linked = 6
}

export interface RpgMaterialTypeUpdate {
  volumeId: number;
  materialType: RpgMaterialType;
}

export interface RpgGeekSearchResult {
  id: number;
  name: string;
  yearPublished: number | null;
}

export interface RpgGeekProduct {
  id: number;
  title: string;
  yearPublished: number | null;
  description: string | null;
  designers: string[];
  publishers: string[];
  imageUrl: string | null;
}

export interface RpgGeekCandidateSearchResult {
  succeeded: boolean;
  error: number;
  status: RpgGeekMatchStatus;
  candidates: RpgGeekSearchResult[];
}

export interface RpgGeekCandidatePreview {
  product: RpgGeekProduct;
  fingerprint: string;
}

export interface ApplyRpgGeekCandidate {
  volumeId: number;
  productId: number;
  previewFingerprint: string;
  replaceTitle: boolean;
  replaceSummary: boolean;
  replaceYear: boolean;
  replaceWriters: boolean;
  replacePublishers: boolean;
  replaceCover: boolean;
}

export interface ApplyRpgGeekCandidatesBatch {
  seriesId: number;
  items: ApplyRpgGeekCandidate[];
}

export interface RpgReviewItem {
  volume: Volume;
  fileNames: string[];
  fileCount: number;
}
