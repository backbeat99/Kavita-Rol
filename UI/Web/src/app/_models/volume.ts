import {Chapter} from './chapter';
import {HourEstimateRange} from './series-detail/hour-estimate-range';
import {IHasCover} from "./common/i-has-cover";
import {IHasReadingTime} from "./common/i-has-reading-time";
import {IHasProgress} from "./common/i-has-progress";
import {IHasMetadataIds} from "./common/i-has-metadata-ids";
import {RpgMaterialType} from "./library/rpg-material-type";
import {DriveThruRpgMatchStatus} from "./library/drive-thru-rpg-match-status";

export interface Volume extends IHasCover, IHasReadingTime, IHasProgress, IHasMetadataIds {
  id: number;
  seriesId: number;
  minNumber: number;
  maxNumber: number;
  name: string;
  createdUtc: string;
  lastModifiedUtc: string;
  pages: number;
  pagesRead: number;
  wordCount: number;
  chapters: Array<Chapter>;
  /**
   * This is only available on the object when fetched for SeriesDetail
   */
  timeEstimate?: HourEstimateRange;
  minHoursToRead: number;
  maxHoursToRead: number;
  avgHoursToRead: number;

  coverImage?: string;
  coverImageLocked: boolean;
  primaryColor: string;
  secondaryColor: string;

  aniListId: number;
  malId: number;
  hardcoverId: number;
  metronId: number;
  comicVineId: string | null;
  mangaBakaId: number;
  cbrId: number;
  driveThruRpgId?: number | null;
  rpgMaterialType: RpgMaterialType;
  driveThruRpgMatchStatus: DriveThruRpgMatchStatus;
  driveThruRpgLastCheckedUtc?: string | null;
  rpgGeekId?: number | null;
  rpgGeekMatchStatus: DriveThruRpgMatchStatus;
  rpgGeekLastCheckedUtc?: string | null;

  nameLocked: boolean;
  summary: string;
  summaryLocked: boolean;
  releaseDate?: string | null;
  releaseDateLocked: boolean;
  language: string;
  languageLocked: boolean;
}
